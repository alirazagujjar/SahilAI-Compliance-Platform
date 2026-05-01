using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SahilAI.Domain.Documents;
using SahilAI.Domain.Entities;
using SahilAI.Domain.Interfaces;

namespace SahilAI.Infrastructure.AI;

public sealed class SemanticKernelComplianceAgent : IComplianceAgent
{
    private readonly Kernel _kernel;
    private readonly ILogger<SemanticKernelComplianceAgent> _logger;

    public SemanticKernelComplianceAgent(Kernel kernel, ILogger<SemanticKernelComplianceAgent> logger)
    {
        _kernel = kernel;
        _logger = logger;
    }

    public async Task<Invoice?> ExtractAndValidateAsync(DocumentContent document, string sourceFile, string region)
    {
        var chatService = _kernel.GetRequiredService<IChatCompletionService>();

        var history = new ChatHistory();
        history.AddSystemMessage(ComplianceAgentPrompts.ExtractionSystem);

        if (document.IsScannedPdf)
        {
            _logger.LogWarning("Scanned PDF detected for {File} — no selectable text. Routing to Review.", sourceFile);
            return BuildScannedPdfPlaceholder(sourceFile, region);
        }

        if (document.HasImage)
        {
            _logger.LogInformation("Sending image ({Mime}) to vision LLM: {File}", document.MimeType, sourceFile);
            AddImageMessage(history, document, region);

            try
            {
                var imgResponse = await chatService.GetChatMessageContentAsync(history);
                var imgJson     = imgResponse.Content ?? string.Empty;
                _logger.LogDebug("Vision LLM raw response: {Json}", imgJson);
                return ParseInvoice(imgJson, sourceFile, region);
            }
            catch (HttpOperationException ex) when ((int?)ex.StatusCode == 400)
            {
                _logger.LogWarning(
                    "Vision LLM rejected image with 400 for {File} — model does not support vision input. " +
                    "Routing to Review for manual processing.", sourceFile);
                return BuildVisionUnsupportedPlaceholder(sourceFile, region);
            }
        }

        _logger.LogInformation("Sending text ({Pages} page(s)) to LLM: {File}", document.PageCount, sourceFile);
        history.AddUserMessage(ComplianceAgentPrompts.BuildExtractionPrompt(document.Text!, region));

        var response = await chatService.GetChatMessageContentAsync(history);
        var rawJson  = response.Content ?? string.Empty;

        _logger.LogDebug("LLM raw response: {Json}", rawJson);

        return ParseInvoice(rawJson, sourceFile, region);
    }

    private static void AddImageMessage(ChatHistory history, DocumentContent document, string region)
    {
        var items = new ChatMessageContentItemCollection
        {
            new TextContent(ComplianceAgentPrompts.BuildImageExtractionPrompt(region)),
            new ImageContent(document.ImageBytes!, document.MimeType)
        };
        history.AddUserMessage(items);
    }

    private static Invoice BuildScannedPdfPlaceholder(string sourceFile, string region) => new()
    {
        InvoiceNumber    = $"SCAN-{Path.GetFileNameWithoutExtension(sourceFile)}",
        VendorName       = "Unknown (Scanned PDF)",
        SourceFile       = sourceFile,
        Region           = region,
        Status           = InvoiceStatus.Review,
        ConfidenceScore  = 0.10m,
        DocumentLanguage = "UNKNOWN",
        ReasoningLog     = """{"confidence":0.10,"reasoning":"Scanned PDF — no selectable text found. Manual review required."}"""
    };

    private static Invoice BuildVisionUnsupportedPlaceholder(string sourceFile, string region) => new()
    {
        InvoiceNumber    = $"IMG-{Path.GetFileNameWithoutExtension(sourceFile)}",
        VendorName       = "Unknown (Image — Vision Not Supported)",
        SourceFile       = sourceFile,
        Region           = region,
        Status           = InvoiceStatus.NotSupportedFormat,
        ConfidenceScore  = 0.00m,
        DocumentLanguage = "UNKNOWN",
        ReasoningLog     = """{"confidence":0.00,"reasoning":"Image received but the configured LLM does not support vision input. File moved to invalidformat folder."}"""
    };

    private Invoice? ParseInvoice(string json, string sourceFile, string region)
    {
        try
        {
            var cleaned  = ExtractAndRepairJson(json, out var wasRepaired);
            if (wasRepaired)
                _logger.LogWarning("LLM response was truncated — JSON repaired before parsing for {File}", sourceFile);

            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            var confidence = root.GetDecimalOrZero("confidence");
            var language   = root.GetStringOrEmpty("document_language", "EN");

            _logger.LogInformation("Document language detected: {Language}", language);

            var invoice = new Invoice
            {
                InvoiceNumber    = root.GetStringOrEmpty("invoice_number"),
                VendorName       = root.GetStringOrEmpty("vendor_name"),
                VendorTrn        = NormalizeTrn(root.GetStringOrEmpty("vendor_trn")),
                InvoiceDate      = root.TryGetProperty("invoice_date", out var dateEl)
                    ? DateTime.TryParse(dateEl.GetString(), out var dt) ? dt : DateTime.UtcNow
                    : DateTime.UtcNow,
                Subtotal         = root.GetDecimalOrZero("subtotal"),
                TaxAmount        = root.GetDecimalOrZero("tax_amount"),
                GrandTotal       = root.GetDecimalOrZero("grand_total"),
                TaxRate          = root.GetDecimalOrZero("tax_rate"),
                Currency         = root.GetStringOrEmpty("currency", "AED"),
                ConfidenceScore  = confidence,
                DocumentLanguage = language,
                Region           = region,
                SourceFile       = sourceFile,
                ReasoningLog     = BuildReasoningLog(root, confidence, language),
                ZatcaUuid        = region is ComplianceRegion.SaudiArabia ? GenerateUuid() : null,
                PreviousInvoiceHash = region is ComplianceRegion.SaudiArabia ? GeneratePih(sourceFile) : null,
            };

            return invoice;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse LLM JSON response");
            return null;
        }
    }

    private static string ExtractAndRepairJson(string raw, out bool wasRepaired)
    {
        wasRepaired = false;
        var start = raw.IndexOf('{');
        if (start < 0) return raw;

        // Walk the JSON tracking open structures — if we reach depth 0 the object is complete.
        var stack    = new Stack<char>();
        var inString = false;
        var escape   = false;

        for (var i = start; i < raw.Length; i++)
        {
            var c = raw[i];

            if (escape)   { escape = false; continue; }
            if (c == '\\' && inString) { escape = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;

            if      (c == '{') stack.Push('}');
            else if (c == '[') stack.Push(']');
            else if ((c == '}' || c == ']') && stack.Count > 0)
            {
                stack.Pop();
                if (stack.Count == 0)
                    return raw[start..(i + 1)]; // complete — no repair needed
            }
        }

        // JSON was truncated — repair it.
        wasRepaired = true;
        var truncated = raw[start..].TrimEnd().TrimEnd(',');

        // Close any dangling string literal.
        if (inString) truncated += '"';

        // Close every unclosed structure in reverse order.
        var sb = new StringBuilder(truncated);
        while (stack.Count > 0)
            sb.Append(stack.Pop());

        return sb.ToString();
    }

    private static string BuildReasoningLog(JsonElement root, decimal confidence, string language)
    {
        var reasoning      = root.TryGetProperty("reasoning",       out var r)   ? r.GetString() ?? ""  : "";
        var lineItemsRaw   = root.TryGetProperty("line_items",      out var li)  ? li.GetRawText()      : "[]";
        var fieldEvidence  = root.TryGetProperty("field_evidence",  out var fe)  ? fe.GetRawText()      : "{}";
        var vendorNameAr   = root.TryGetProperty("vendor_name_ar",  out var vna) ? vna.GetString()      : null;

        return JsonSerializer.Serialize(new
        {
            confidence,
            document_language = language,
            vendor_name_ar    = vendorNameAr,
            reasoning,
            field_evidence    = JsonSerializer.Deserialize<object>(fieldEvidence),
            line_items        = JsonSerializer.Deserialize<object>(lineItemsRaw)
        });
    }

    // Strip spaces, dashes, and dots from TRN so "100 378 294 500 003" → "100378294500003"
    private static string NormalizeTrn(string trn) =>
        string.IsNullOrWhiteSpace(trn) ? trn : new string(trn.Where(char.IsDigit).ToArray());

    private static string GenerateUuid() => Guid.NewGuid().ToString("D").ToUpperInvariant();

    private static string GeneratePih(string sourceFile)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sourceFile + DateTime.UtcNow.ToString("yyyyMMdd")));
        return Convert.ToBase64String(bytes);
    }
}

file static class JsonElementExtensions
{
    public static string GetStringOrEmpty(this JsonElement el, string property, string defaultValue = "")
    {
        if (el.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString() ?? defaultValue;
        return defaultValue;
    }

    public static decimal GetDecimalOrZero(this JsonElement el, string property)
    {
        if (el.TryGetProperty(property, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var val))
                return val;
        }
        return 0m;
    }
}
