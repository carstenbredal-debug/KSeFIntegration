using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using KSeF.Functions.Models;

namespace KSeF.Functions.Services;

/// <summary>
/// Client for the KSeF 2.0 (Krajowy System e-Faktur) REST API.
/// Implements the v2 authentication, session management, and invoice
/// submission flow with RSA-encrypted tokens and AES-encrypted invoices.
/// </summary>
public class KSeFApiClient
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<KSeFApiClient> _logger;
    private readonly JsonSerializerOptions _jsonOpts;

    private string? _accessToken;
    private string? _sessionReferenceNumber;
    private byte[]? _aesKey;
    private byte[]? _aesIv;

    public KSeFApiClient(HttpClient http, IConfiguration config, ILogger<KSeFApiClient> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
        _jsonOpts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    private string BaseUrl => _config["KSeF:BaseUrl"] ?? "https://api-demo.ksef.mf.gov.pl/v2";
    private string Token => _config["KSeF:Token"] ?? "";

    /// <summary>
    /// Authenticate with KSeF v2 using a KSeF token.
    /// 1. POST /auth/challenge
    /// 2. GET /security/public-key-certificates
    /// 3. Encrypt token with RSA-OAEP
    /// 4. POST /auth/ksef-token
    /// 5. Poll GET /auth/{ref} until accessToken is ready
    /// </summary>
    public async Task<KSeFSessionResponse> InitSessionAsync(string nip)
    {
        _logger.LogInformation("KSeF v2 auth: starting for NIP {NIP}", nip);

        // Step 1: Get challenge
        var challengeResp = await _http.PostAsync($"{BaseUrl}/auth/challenge", null);
        var challengeBody = await challengeResp.Content.ReadAsStringAsync();
        if (!challengeResp.IsSuccessStatusCode)
            throw new Exception($"KSeF auth challenge failed: {challengeResp.StatusCode} - {challengeBody}");

        using var challengeDoc = JsonDocument.Parse(challengeBody);
        var challenge = challengeDoc.RootElement.GetProperty("challenge").GetString()!;
        var timestampMs = challengeDoc.RootElement.GetProperty("timestampMs").GetInt64();
        _logger.LogInformation("KSeF challenge obtained: {Challenge}", challenge);

        // Step 2: Get encryption certificate
        var certsResp = await _http.GetAsync($"{BaseUrl}/security/public-key-certificates");
        var certsBody = await certsResp.Content.ReadAsStringAsync();
        if (!certsResp.IsSuccessStatusCode)
            throw new Exception($"KSeF cert fetch failed: {certsResp.StatusCode} - {certsBody}");

        using var certsDoc = JsonDocument.Parse(certsBody);
        string? tokenCertB64 = null;
        string? tokenPublicKeyId = null;
        string? symmetricCertB64 = null;
        string? symmetricPublicKeyId = null;
        string latestTokenValidFrom = "";
        string latestSymValidFrom = "";

        foreach (var cert in certsDoc.RootElement.EnumerateArray())
        {
            var usages = cert.GetProperty("usage");
            foreach (var u in usages.EnumerateArray())
            {
                var usage = u.GetString();
                if (usage == "KsefTokenEncryption")
                {
                    var vf = cert.GetProperty("validFrom").GetString() ?? "";
                    if (string.Compare(vf, latestTokenValidFrom, StringComparison.Ordinal) > 0)
                    {
                        latestTokenValidFrom = vf;
                        tokenCertB64 = cert.GetProperty("certificate").GetString();
                        tokenPublicKeyId = cert.GetProperty("publicKeyId").GetString();
                    }
                }
                if (usage == "SymmetricKeyEncryption")
                {
                    var vf = cert.GetProperty("validFrom").GetString() ?? "";
                    if (string.Compare(vf, latestSymValidFrom, StringComparison.Ordinal) > 0)
                    {
                        latestSymValidFrom = vf;
                        symmetricCertB64 = cert.GetProperty("certificate").GetString();
                        symmetricPublicKeyId = cert.GetProperty("publicKeyId").GetString();
                    }
                }
            }
        }

        if (tokenCertB64 == null || tokenPublicKeyId == null)
            throw new Exception("No KsefTokenEncryption certificate found");
        if (symmetricCertB64 == null || symmetricPublicKeyId == null)
            throw new Exception("No SymmetricKeyEncryption certificate found");

        // Step 3: Encrypt token|timestampMs with RSA-OAEP SHA-256
        var payload = $"{Token}|{timestampMs}";
        var certDer = Convert.FromBase64String(tokenCertB64);
        var x509 = new X509Certificate2(certDer);
        var rsa = x509.GetRSAPublicKey()
            ?? throw new Exception("Certificate does not have an RSA public key");
        var encryptedToken = rsa.Encrypt(
            Encoding.UTF8.GetBytes(payload),
            RSAEncryptionPadding.OaepSHA256);
        var encryptedTokenB64 = Convert.ToBase64String(encryptedToken);

        // Step 4: POST /auth/ksef-token
        var authBody = new
        {
            challenge,
            contextIdentifier = new { type = "nip", value = nip },
            encryptedToken = encryptedTokenB64,
            publicKeyId = tokenPublicKeyId
        };
        var authJson = JsonSerializer.Serialize(authBody, _jsonOpts);
        var authResp = await _http.PostAsync($"{BaseUrl}/auth/ksef-token",
            new StringContent(authJson, Encoding.UTF8, "application/json"));
        var authRespBody = await authResp.Content.ReadAsStringAsync();

        if (authResp.StatusCode != System.Net.HttpStatusCode.Accepted &&
            authResp.StatusCode != System.Net.HttpStatusCode.OK)
            throw new Exception($"KSeF token auth failed: {authResp.StatusCode} - {authRespBody}");

        using var authDoc = JsonDocument.Parse(authRespBody);
        var referenceNumber = authDoc.RootElement.GetProperty("referenceNumber").GetString()!;
        var authenticationToken = authDoc.RootElement
            .GetProperty("authenticationToken")
            .GetProperty("token").GetString()!;

        _logger.LogInformation("KSeF auth submitted, ref: {Ref}", referenceNumber);

        // Step 5: Poll GET /auth/{ref} for accessToken
        _accessToken = null;
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(3000);
            var statusReq = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/auth/{referenceNumber}");
            statusReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authenticationToken);
            var statusResp = await _http.SendAsync(statusReq);
            var statusBody = await statusResp.Content.ReadAsStringAsync();

            using var statusDoc = JsonDocument.Parse(statusBody);
            if (statusDoc.RootElement.TryGetProperty("status", out var status))
            {
                var code = status.GetProperty("code").GetInt32();
                if (code == 200)
                {
                    _accessToken = statusDoc.RootElement
                        .GetProperty("accessToken")
                        .GetProperty("token").GetString();
                    _logger.LogInformation("KSeF auth succeeded");
                    break;
                }
                if (code >= 400)
                {
                    var desc = status.TryGetProperty("description", out var d) ? d.GetString() : "unknown";
                    var details = status.TryGetProperty("details", out var det)
                        ? string.Join("; ", det.EnumerateArray().Select(x => x.GetString()))
                        : "";
                    throw new Exception($"KSeF auth failed (code {code}): {desc}. {details}");
                }
            }
        }

        if (_accessToken == null)
            throw new Exception("KSeF auth timed out waiting for accessToken");

        // Step 6: Open interactive session
        _aesKey = RandomNumberGenerator.GetBytes(32);
        _aesIv = RandomNumberGenerator.GetBytes(16);

        var symCertDer = Convert.FromBase64String(symmetricCertB64);
        var symX509 = new X509Certificate2(symCertDer);
        var symRsa = symX509.GetRSAPublicKey()
            ?? throw new Exception("Symmetric certificate does not have an RSA public key");
        var encryptedKey = symRsa.Encrypt(_aesKey, RSAEncryptionPadding.OaepSHA256);

        var sessionBody = new
        {
            formCode = new
            {
                systemCode = "FA (2)",
                schemaVersion = "1-0E",
                value = "FA"
            },
            encryption = new
            {
                encryptedSymmetricKey = Convert.ToBase64String(encryptedKey),
                initializationVector = Convert.ToBase64String(_aesIv),
                publicKeyId = symmetricPublicKeyId
            }
        };
        var sessionJson = JsonSerializer.Serialize(sessionBody, _jsonOpts);
        var sessionReq = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/sessions/online")
        {
            Content = new StringContent(sessionJson, Encoding.UTF8, "application/json")
        };
        sessionReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var sessionResp = await _http.SendAsync(sessionReq);
        var sessionRespBody = await sessionResp.Content.ReadAsStringAsync();

        if (!sessionResp.IsSuccessStatusCode)
        {
            _logger.LogError("KSeF session open failed: {Status} {Body}", sessionResp.StatusCode, sessionRespBody);
            throw new Exception($"KSeF session open failed: {sessionResp.StatusCode} - {sessionRespBody}");
        }

        using var sessionDoc = JsonDocument.Parse(sessionRespBody);
        _sessionReferenceNumber = sessionDoc.RootElement
            .GetProperty("referenceNumber").GetString()!;

        _logger.LogInformation("KSeF session opened: {Ref}", _sessionReferenceNumber);

        return new KSeFSessionResponse
        {
            SessionToken = _accessToken,
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Send an encrypted invoice XML within an active v2 session.
    /// </summary>
    public async Task<KSeFSendResponse> SendInvoiceAsync(string invoiceXml, string? sessionToken = null)
    {
        var token = sessionToken ?? _accessToken
            ?? throw new InvalidOperationException("No active KSeF session. Call InitSessionAsync first.");
        var sessionRef = _sessionReferenceNumber
            ?? throw new InvalidOperationException("No active KSeF session reference.");

        if (_aesKey == null || _aesIv == null)
            throw new InvalidOperationException("No encryption keys. Call InitSessionAsync first.");

        var invoiceBytes = Encoding.UTF8.GetBytes(invoiceXml);

        // Compute hashes on plain invoice
        var invoiceHash = Convert.ToBase64String(SHA256.HashData(invoiceBytes));
        var invoiceSize = invoiceBytes.Length;

        // Encrypt with AES-256-CBC
        byte[] encryptedInvoice;
        using (var aes = Aes.Create())
        {
            aes.KeySize = 256;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = _aesKey;
            aes.IV = _aesIv;
            using var encryptor = aes.CreateEncryptor();
            encryptedInvoice = encryptor.TransformFinalBlock(invoiceBytes, 0, invoiceBytes.Length);
        }

        var encryptedInvoiceHash = Convert.ToBase64String(SHA256.HashData(encryptedInvoice));
        var encryptedInvoiceSize = encryptedInvoice.Length;

        var sendBody = new
        {
            invoiceHash,
            invoiceSize,
            encryptedInvoiceHash,
            encryptedInvoiceSize,
            encryptedInvoiceContent = Convert.ToBase64String(encryptedInvoice)
        };

        var sendJson = JsonSerializer.Serialize(sendBody, _jsonOpts);
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/sessions/online/{sessionRef}/invoices/")
        {
            Content = new StringContent(sendJson, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        _logger.LogInformation("Sending encrypted invoice to KSeF session {Ref}", sessionRef);

        var response = await _http.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("KSeF invoice send failed: {Status} {Body}", response.StatusCode, responseBody);
            throw new Exception($"KSeF invoice send failed: {response.StatusCode} - {responseBody}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        return new KSeFSendResponse
        {
            ElementReferenceNumber = root.GetProperty("referenceNumber").GetString() ?? "",
            ProcessingTimestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Check the status of a submitted invoice via session documents.
    /// </summary>
    public async Task<KSeFInvoiceStatus> GetInvoiceStatusAsync(string elementReferenceNumber, string? sessionToken = null)
    {
        var token = sessionToken ?? _accessToken
            ?? throw new InvalidOperationException("No active KSeF session.");
        var sessionRef = _sessionReferenceNumber
            ?? throw new InvalidOperationException("No active KSeF session reference.");

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/sessions/online/{sessionRef}/invoices/{elementReferenceNumber}/status");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        _logger.LogInformation("Checking KSeF invoice status for {Ref}", elementReferenceNumber);

        var response = await _http.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("KSeF status check failed: {Status} {Body}", response.StatusCode, responseBody);
            throw new Exception($"KSeF status check failed: {response.StatusCode} - {responseBody}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        return new KSeFInvoiceStatus
        {
            ElementReferenceNumber = elementReferenceNumber,
            ProcessingCode = root.TryGetProperty("processingCode", out var pc) ? pc.GetInt32() : 0,
            ProcessingDescription = root.TryGetProperty("processingDescription", out var pd) ? pd.GetString() : null,
            KSeFReferenceNumber = root.TryGetProperty("ksefReferenceNumber", out var kr) ? kr.GetString() : null,
            AcquisitionTimestamp = root.TryGetProperty("acquisitionTimestamp", out var at) ? at.GetDateTime() : null
        };
    }

    /// <summary>
    /// Close the active KSeF interactive session.
    /// </summary>
    public async Task TerminateSessionAsync(string? sessionToken = null)
    {
        var token = sessionToken ?? _accessToken;
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(_sessionReferenceNumber))
            return;

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/sessions/online/{_sessionReferenceNumber}/close");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        _logger.LogInformation("Closing KSeF session {Ref}", _sessionReferenceNumber);

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("KSeF session close failed: {Status} {Body}", response.StatusCode, body);
        }

        _accessToken = null;
        _sessionReferenceNumber = null;
        _aesKey = null;
        _aesIv = null;
    }
}
