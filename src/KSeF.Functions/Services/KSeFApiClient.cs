using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using KSeF.Functions.Models;

namespace KSeF.Functions.Services;

/// <summary>
/// Client for the KSeF (Krajowy System e-Faktur) REST API.
/// Handles session management and invoice submission/status checking.
/// </summary>
public class KSeFApiClient
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<KSeFApiClient> _logger;
    private readonly JsonSerializerOptions _jsonOpts;

    private string? _sessionToken;

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

    private string BaseUrl => _config["KSeF:BaseUrl"] ?? "https://ksef-demo.mf.gov.pl/api";
    private string Token => _config["KSeF:Token"] ?? "";

    /// <summary>
    /// Start an interactive session with KSeF using a token.
    /// </summary>
    public async Task<KSeFSessionResponse> InitSessionAsync(string nip)
    {
        var requestBody = new
        {
            context = new
            {
                challenge = Guid.NewGuid().ToString("N"),
                identifier = new
                {
                    type = "onip",
                    identifier = nip
                },
                documentType = new
                {
                    service = "KSeF",
                    formCode = new
                    {
                        systemCode = "FA (2)",
                        schemaVersion = "1-0E",
                        targetNamespace = "http://crd.gov.pl/wzor/2023/06/29/12648/",
                        value = "FA"
                    }
                },
                token = Token
            }
        };

        var json = JsonSerializer.Serialize(requestBody, _jsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("Initiating KSeF session for NIP {NIP}", nip);

        var response = await _http.PostAsync($"{BaseUrl}/online/Session/InitToken", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("KSeF session init failed: {Status} {Body}", response.StatusCode, responseBody);
            throw new Exception($"KSeF session init failed: {response.StatusCode} - {responseBody}");
        }

        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        _sessionToken = root.GetProperty("sessionToken").GetProperty("token").GetString();

        return new KSeFSessionResponse
        {
            SessionToken = _sessionToken ?? "",
            Timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Send an invoice XML to KSeF within an active session.
    /// </summary>
    public async Task<KSeFSendResponse> SendInvoiceAsync(string invoiceXml, string? sessionToken = null)
    {
        var token = sessionToken ?? _sessionToken
            ?? throw new InvalidOperationException("No active KSeF session. Call InitSessionAsync first.");

        var content = new StringContent(invoiceXml, Encoding.UTF8, "application/octet-stream");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var request = new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}/online/Invoice/Send")
        {
            Content = content
        };
        request.Headers.Add("SessionToken", token);

        _logger.LogInformation("Sending invoice to KSeF");

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
            ElementReferenceNumber = root.GetProperty("elementReferenceNumber").GetString() ?? "",
            ProcessingTimestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Check the status of a submitted invoice.
    /// </summary>
    public async Task<KSeFInvoiceStatus> GetInvoiceStatusAsync(string elementReferenceNumber, string? sessionToken = null)
    {
        var token = sessionToken ?? _sessionToken
            ?? throw new InvalidOperationException("No active KSeF session.");

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/online/Invoice/Status/{elementReferenceNumber}");
        request.Headers.Add("SessionToken", token);

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
            ProcessingCode = root.GetProperty("processingCode").GetInt32(),
            ProcessingDescription = root.TryGetProperty("processingDescription", out var pd) ? pd.GetString() : null,
            KSeFReferenceNumber = root.TryGetProperty("ksefReferenceNumber", out var kr) ? kr.GetString() : null,
            AcquisitionTimestamp = root.TryGetProperty("acquisitionTimestamp", out var at) ? at.GetDateTime() : null
        };
    }

    /// <summary>
    /// Terminate the active KSeF session.
    /// </summary>
    public async Task TerminateSessionAsync(string? sessionToken = null)
    {
        var token = sessionToken ?? _sessionToken;
        if (string.IsNullOrEmpty(token)) return;

        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/online/Session/Terminate");
        request.Headers.Add("SessionToken", token);

        _logger.LogInformation("Terminating KSeF session");

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("KSeF session terminate failed: {Status} {Body}", response.StatusCode, body);
        }

        _sessionToken = null;
    }
}
