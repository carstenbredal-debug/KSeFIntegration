using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using KSeF.Functions.Models;
using KSeF.Functions.Services;

namespace KSeF.Functions.Functions;

/// <summary>
/// Azure Function endpoints for KSeF invoice operations.
/// Called by Business Central to submit invoices and check status.
/// </summary>
public class InvoiceFunctions
{
    private readonly KSeFApiClient _ksef;
    private readonly InvoiceXmlBuilder _xmlBuilder;
    private readonly ILogger<InvoiceFunctions> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public InvoiceFunctions(KSeFApiClient ksef, InvoiceXmlBuilder xmlBuilder, ILogger<InvoiceFunctions> logger)
    {
        _ksef = ksef;
        _xmlBuilder = xmlBuilder;
        _logger = logger;
    }

    /// <summary>
    /// Submit an invoice to KSeF.
    /// POST /api/invoice/submit
    /// Body: InvoiceData JSON
    /// Returns: SubmitResult with element reference number
    /// </summary>
    [Function("SubmitInvoice")]
    public async Task<HttpResponseData> SubmitInvoice(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "invoice/submit")] HttpRequestData req)
    {
        _logger.LogInformation("SubmitInvoice called");

        try
        {
            var body = await req.ReadAsStringAsync();
            var invoice = JsonSerializer.Deserialize<InvoiceData>(body!, JsonOpts);

            if (invoice == null)
                return await CreateResponse(req, HttpStatusCode.BadRequest,
                    new SubmitResult { Success = false, Error = "Invalid invoice data" });

            // 1. Build FA(2) XML
            var xml = _xmlBuilder.Build(invoice);
            _logger.LogInformation("Generated XML for invoice {Number}", invoice.InvoiceNumber);

            // 2. Init KSeF session
            var session = await _ksef.InitSessionAsync(invoice.Seller.NIP);
            _logger.LogInformation("KSeF session started: {Token}", session.SessionToken[..8] + "...");

            // 3. Send invoice
            var sendResult = await _ksef.SendInvoiceAsync(xml, session.SessionToken);
            _logger.LogInformation("Invoice sent, ref: {Ref}", sendResult.ElementReferenceNumber);

            // 4. Terminate session
            await _ksef.TerminateSessionAsync(session.SessionToken);

            return await CreateResponse(req, HttpStatusCode.OK, new SubmitResult
            {
                Success = true,
                ElementReferenceNumber = sendResult.ElementReferenceNumber,
                SessionToken = session.SessionToken
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SubmitInvoice failed");
            return await CreateResponse(req, HttpStatusCode.InternalServerError,
                new SubmitResult { Success = false, Error = ex.Message });
        }
    }

    /// <summary>
    /// Check the status of a previously submitted invoice.
    /// GET /api/invoice/status/{elementReferenceNumber}?nip={sellerNip}
    /// Returns: StatusResult with processing code and KSeF reference number
    /// </summary>
    [Function("GetInvoiceStatus")]
    public async Task<HttpResponseData> GetInvoiceStatus(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "invoice/status/{elementReferenceNumber}")] HttpRequestData req,
        string elementReferenceNumber)
    {
        _logger.LogInformation("GetInvoiceStatus called for {Ref}", elementReferenceNumber);

        try
        {
            var nip = req.Query["nip"];
            if (string.IsNullOrEmpty(nip))
                return await CreateResponse(req, HttpStatusCode.BadRequest,
                    new StatusResult { Success = false, Error = "Missing 'nip' query parameter" });

            // Init session to check status
            var session = await _ksef.InitSessionAsync(nip);

            var status = await _ksef.GetInvoiceStatusAsync(elementReferenceNumber, session.SessionToken);

            await _ksef.TerminateSessionAsync(session.SessionToken);

            return await CreateResponse(req, HttpStatusCode.OK, new StatusResult
            {
                Success = true,
                ProcessingCode = status.ProcessingCode,
                ProcessingDescription = status.ProcessingDescription,
                KSeFReferenceNumber = status.KSeFReferenceNumber,
                AcquisitionTimestamp = status.AcquisitionTimestamp
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetInvoiceStatus failed");
            return await CreateResponse(req, HttpStatusCode.InternalServerError,
                new StatusResult { Success = false, Error = ex.Message });
        }
    }

    /// <summary>
    /// Generate invoice XML preview without sending to KSeF.
    /// POST /api/invoice/preview
    /// Body: InvoiceData JSON
    /// Returns: FA(2) XML string
    /// </summary>
    [Function("PreviewInvoiceXml")]
    public async Task<HttpResponseData> PreviewInvoiceXml(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "invoice/preview")] HttpRequestData req)
    {
        _logger.LogInformation("PreviewInvoiceXml called");

        try
        {
            var body = await req.ReadAsStringAsync();
            var invoice = JsonSerializer.Deserialize<InvoiceData>(body!, JsonOpts);

            if (invoice == null)
            {
                var badResp = req.CreateResponse(HttpStatusCode.BadRequest);
                await badResp.WriteStringAsync("Invalid invoice data");
                return badResp;
            }

            var xml = _xmlBuilder.Build(invoice);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/xml");
            await response.WriteStringAsync(xml);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PreviewInvoiceXml failed");
            var errResp = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errResp.WriteStringAsync(ex.Message);
            return errResp;
        }
    }

    /// <summary>
    /// Health check endpoint.
    /// GET /api/health
    /// </summary>
    [Function("HealthCheck")]
    public async Task<HttpResponseData> HealthCheck(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(JsonSerializer.Serialize(new
        {
            status = "healthy",
            service = "KSeFIntegration",
            timestamp = DateTime.UtcNow
        }));
        return response;
    }

    private static async Task<HttpResponseData> CreateResponse<T>(HttpRequestData req, HttpStatusCode status, T body)
    {
        var response = req.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, JsonOpts));
        return response;
    }
}
