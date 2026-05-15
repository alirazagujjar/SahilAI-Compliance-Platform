using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SahilAI.Application.DTOs;
using SahilAI.Application.Interfaces;
using SahilAI.Application.Services;
using SahilAI.Domain.Documents;
using SahilAI.Domain.Entities;
using SahilAI.Domain.Interfaces;

namespace SahilAI.Application.Services;

public sealed class InvoiceProcessingService : IInvoiceProcessingService
{
    private readonly IComplianceAgent _agent;
    private readonly IInvoiceRepository _invoices;
    private readonly IAuditLogRepository _auditLogs;
    private readonly IInvoiceLineItemRepository _lineItems;
    private readonly IVendorRepository _vendors;
    private readonly IProcessingQueueRepository _queue;
    private readonly IDocumentReaderFactory _readerFactory;
    private readonly IZatcaComplianceService _zatca;
    private readonly RegionalValidationService _regionalValidator;
    private readonly ILogger<InvoiceProcessingService> _logger;
    private readonly string _xmlOutputPath;
    private readonly string _invoiceSidecarPath;

    public decimal ConfidenceThreshold { get; }

    public InvoiceProcessingService(
        IComplianceAgent agent,
        IInvoiceRepository invoices,
        IAuditLogRepository auditLogs,
        IInvoiceLineItemRepository lineItems,
        IVendorRepository vendors,
        IProcessingQueueRepository queue,
        IDocumentReaderFactory readerFactory,
        IZatcaComplianceService zatca,
        RegionalValidationService regionalValidator,
        ILogger<InvoiceProcessingService> logger,
        string xmlOutputPath = "processed/xml",
        decimal confidenceThreshold = 0.85m,
        string invoiceSidecarPath = "processed/success")
    {
        _agent = agent;
        _invoices = invoices;
        _auditLogs = auditLogs;
        _lineItems = lineItems;
        _vendors = vendors;
        _queue = queue;
        _readerFactory = readerFactory;
        _zatca = zatca;
        _regionalValidator = regionalValidator;
        _logger = logger;
        _xmlOutputPath = xmlOutputPath;
        _invoiceSidecarPath = invoiceSidecarPath;
        ConfidenceThreshold = confidenceThreshold;
    }

    public async Task<ProcessingResultDto> ProcessDocumentAsync(string filePath, string region)
    {
        var sw = Stopwatch.StartNew();
        var fileName = Path.GetFileName(filePath);
        _logger.LogInformation("Processing {File} for region {Region}", fileName, region);

        var tenantId = 1;
        var tenantMatch = System.Text.RegularExpressions.Regex.Match(fileName, @"_t(\d+)");
        if (tenantMatch.Success && int.TryParse(tenantMatch.Groups[1].Value, out var parsed))
            tenantId = parsed;

        DocumentContent document;
        try
        {
            var reader = _readerFactory.GetReader(filePath);
            document   = await reader.ReadAsync(filePath);
            _logger.LogInformation("Read {File} as {FileType} ({Pages} page(s))",
                fileName, document.FileType, document.PageCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read file {File}", filePath);
            return Fail("FILE_READ_ERROR", ex.Message);
        }

        // Track file in ProcessingQueue — now we know the FileType
        var queueItem = new ProcessingQueueItem
        {
            FileName    = fileName,
            FilePath    = filePath,
            FileHash    = ComputeFileHash(filePath),
            Region      = region,
            FileType    = document.FileType,
            QueueStatus = QueueStatus.Processing
        };
        var queueId = await _queue.EnqueueAsync(queueItem);

        Invoice? invoice;
        try
        {
            invoice = await _agent.ExtractAndValidateAsync(document, fileName, region);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent extraction failed for {File}", fileName);
            await LogAudit("ComplianceAgent", "EXTRACT", fileName, "{}", "ERROR", ex.Message, sw.ElapsedMilliseconds);
            await _queue.UpdateAsync(queueId, QueueStatus.Failed, error: ex.Message);
            return Fail("AGENT_ERROR", ex.Message);
        }

        if (invoice is null)
        {
            await LogAudit("ComplianceAgent", "EXTRACT", fileName, "{}", "ERROR", "Agent returned null", sw.ElapsedMilliseconds);
            await _queue.UpdateAsync(queueId, QueueStatus.Failed, error: "Agent returned null");
            return Fail("PARSE_ERROR", "Agent could not extract invoice data from document.");
        }

        // Stamp the file type and tenant onto the invoice
        invoice.FileType = document.FileType;
        invoice.TenantId = tenantId;

        // NOT_SUPPORTED_FORMAT is terminal — skip confidence gate and validation entirely
        var anomalies = new List<string>();

        if (invoice.Status != InvoiceStatus.NotSupportedFormat)
        {
            // Confidence gate — low-confidence invoices go straight to REVIEW
            if (invoice.ConfidenceScore < ConfidenceThreshold)
            {
                _logger.LogWarning(
                    "Low confidence {Score:P0} (threshold {Threshold:P0}) for {File} — routing to Review",
                    invoice.ConfidenceScore, ConfidenceThreshold, fileName);
                invoice.Status = InvoiceStatus.Review;
            }

            anomalies = ComplianceValidationService.Validate(invoice);
            invoice.AnomalyFlags = anomalies.Count > 0 ? JsonSerializer.Serialize(anomalies) : null;

            if (invoice.Status != InvoiceStatus.Review)
                invoice.Status = anomalies.Count > 0 ? InvoiceStatus.Flagged : InvoiceStatus.Valid;

            var existing = await _invoices.GetByInvoiceNumberAsync(invoice.InvoiceNumber);
            if (existing is not null)
            {
                anomalies.Add($"DUPLICATE_INVOICE: Invoice #{invoice.InvoiceNumber} already exists with Id={existing.Id}");
                invoice.Status       = InvoiceStatus.Duplicate;
                invoice.AnomalyFlags = JsonSerializer.Serialize(anomalies);
            }
        }

        int invoiceId;
        try
        {
            invoiceId = await _invoices.InsertAsync(invoice);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist invoice {Number}", invoice.InvoiceNumber);
            await _queue.UpdateAsync(queueId, QueueStatus.Failed, error: ex.Message);
            return Fail("DB_ERROR", ex.Message);
        }

        // Save line items
        await SaveLineItemsAsync(invoice.ReasoningLog, invoiceId);

        // Regional validation (DB-driven rules — runs after insert so invoiceId exists)
        if (invoice.Status != InvoiceStatus.NotSupportedFormat)
        {
            var regionalAnomalies = await _regionalValidator.ValidateAsync(invoice);
            if (regionalAnomalies.Count > 0)
            {
                anomalies.AddRange(regionalAnomalies);
                invoice.AnomalyFlags = JsonSerializer.Serialize(anomalies);
                if (invoice.Status == InvoiceStatus.Valid)
                    invoice.Status = InvoiceStatus.Flagged;
                await _invoices.UpdateStatusAsync(invoiceId, invoice.Status);
            }
        }

        // ZATCA Phase 2 — generate UBL 2.1 XML for Saudi invoices
        if (invoice.Region == ComplianceRegion.SaudiArabia &&
            invoice.Status != InvoiceStatus.NotSupportedFormat)
        {
            var zatcaSw = Stopwatch.StartNew();
            try
            {
                var lineItemsFromDb = await _lineItems.GetByInvoiceIdAsync(invoiceId);
                var prevHash        = await _zatca.GetPreviousHashAsync(invoice.VendorTrn, invoiceId);
                var (xmlContent, xmlHash) = await _zatca.GenerateXmlAsync(invoice, lineItemsFromDb, prevHash);

                // Save to dedicated XML archive folder
                Directory.CreateDirectory(_xmlOutputPath);
                var xmlFileName = $"{invoice.InvoiceNumber.Replace("/", "-").Replace("\\", "-")}_{invoiceId}.xml";
                var xmlFilePath = Path.Combine(_xmlOutputPath, xmlFileName);
                await File.WriteAllTextAsync(xmlFilePath, xmlContent);

                // Also place a copy alongside the original invoice in success/review for auditor access
                var sidecarPath = Path.Combine(_invoiceSidecarPath, xmlFileName);
                Directory.CreateDirectory(_invoiceSidecarPath);
                await File.WriteAllTextAsync(sidecarPath, xmlContent);

                var qrCode = _zatca.GenerateQrCode(invoice);
                await _invoices.UpdateComplianceFieldsAsync(invoiceId, xmlFilePath, xmlHash, qrCode, prevHash);

                zatcaSw.Stop();
                _logger.LogInformation(
                    "ZATCA XML saved: {XmlFile} | QR generated | PIH={Hash} | XmlMs={Ms}ms",
                    xmlFileName, prevHash[..Math.Min(12, prevHash.Length)] + "…", zatcaSw.ElapsedMilliseconds);

                // Dedicated audit entry for ZATCA generation timing
                await LogAudit(
                    "ZatcaComplianceService", "GENERATE_XML",
                    $"InvoiceId={invoiceId} VendorTrn={invoice.VendorTrn}",
                    System.Text.Json.JsonSerializer.Serialize(new
                    {
                        XmlFile        = xmlFileName,
                        XmlHash        = xmlHash,
                        PreviousHash   = prevHash[..Math.Min(16, prevHash.Length)] + "…",
                        IsGenesisBlock = prevHash == "NwZlOopR4nCPVvQ2ubidbc+fBcMVDDjzFMCaFiUVnFI="
                    }),
                    "SUCCESS", null, zatcaSw.ElapsedMilliseconds, invoiceId);
            }
            catch (Exception ex)
            {
                zatcaSw.Stop();
                _logger.LogError(ex, "ZATCA XML generation failed for Invoice {Number}", invoice.InvoiceNumber);
                await LogAudit("ZatcaComplianceService", "GENERATE_XML",
                    $"InvoiceId={invoiceId}", "{}", "ERROR", ex.Message, zatcaSw.ElapsedMilliseconds, invoiceId);
            }
        }

        // Upsert vendor — carry TenantId so the vendor row is always linked to the right tenant
        await _vendors.UpsertAsync(new Vendor
        {
            TenantId      = invoice.TenantId,
            Name          = invoice.VendorName,
            TaxRegNumber  = invoice.VendorTrn,
            Region        = region,
            IsVerified    = false
        });

        // Mark queue item done
        await _queue.UpdateAsync(queueId, QueueStatus.Done, invoiceId: invoiceId);

        sw.Stop();
        await LogAudit("ComplianceAgent", "PROCESS", fileName, invoice.ReasoningLog, invoice.Status, null, sw.ElapsedMilliseconds, invoiceId);

        _logger.LogInformation("Invoice {Number} saved as Id={Id} with status {Status}", invoice.InvoiceNumber, invoiceId, invoice.Status);

        return new ProcessingResultDto(true, invoice.Status, invoiceId, null, anomalies, invoice.ConfidenceScore);
    }

    private async Task SaveLineItemsAsync(string reasoningLog, int invoiceId)
    {
        try
        {
            using var doc = JsonDocument.Parse(reasoningLog);
            if (!doc.RootElement.TryGetProperty("line_items", out var lineItemsEl)) return;

            var items = new List<InvoiceLineItem>();
            short lineNumber = 1;

            foreach (var li in lineItemsEl.EnumerateArray())
            {
                items.Add(new InvoiceLineItem
                {
                    InvoiceId   = invoiceId,
                    LineNumber  = lineNumber++,
                    Description = li.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                    Quantity    = li.TryGetProperty("quantity",    out var q) && q.TryGetDecimal(out var qv) ? qv : 1,
                    UnitPrice   = li.TryGetProperty("unit_price",  out var u) && u.TryGetDecimal(out var uv) ? uv : 0,
                    LineTotal   = li.TryGetProperty("line_total",  out var t) && t.TryGetDecimal(out var tv) ? tv : 0,
                });
            }

            if (items.Count > 0)
                await _lineItems.InsertBatchAsync(items);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not parse line items from reasoning log for InvoiceId={Id}", invoiceId);
        }
    }

    private async Task LogAudit(string agent, string action, string input, string output, string status, string? error, long ms, int? invoiceId = null)
    {
        await _auditLogs.InsertAsync(new AgentAuditLog
        {
            AgentName    = agent,
            Action       = action,
            InputSummary = input,
            OutputJson   = output,
            Status       = status,
            ErrorMessage = error,
            DurationMs   = ms,
            InvoiceId    = invoiceId
        });
    }

    private static string? ComputeFileHash(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch { return null; }
    }

    private static ProcessingResultDto Fail(string status, string message) =>
        new(false, status, null, message, []);
}
