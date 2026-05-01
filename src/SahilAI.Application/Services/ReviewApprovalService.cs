using Microsoft.Extensions.Logging;
using SahilAI.Application.DTOs;
using SahilAI.Application.Interfaces;
using SahilAI.Domain.Entities;
using SahilAI.Domain.Interfaces;

namespace SahilAI.Application.Services;

public sealed class ReviewApprovalService : IReviewApprovalService
{
    private readonly IInvoiceRepository _invoices;
    private readonly IInvoiceLineItemRepository _lineItems;
    private readonly IZatcaComplianceService _zatca;
    private readonly ILogger<ReviewApprovalService> _logger;
    private readonly string _xmlOutputPath;
    private readonly string _successPath;
    private readonly string _reviewPath;

    public ReviewApprovalService(
        IInvoiceRepository invoices,
        IInvoiceLineItemRepository lineItems,
        IZatcaComplianceService zatca,
        ILogger<ReviewApprovalService> logger,
        string xmlOutputPath,
        string successPath,
        string reviewPath)
    {
        _invoices      = invoices;
        _lineItems     = lineItems;
        _zatca         = zatca;
        _logger        = logger;
        _xmlOutputPath = xmlOutputPath;
        _successPath   = successPath;
        _reviewPath    = reviewPath;
    }

    public async Task<ApprovalResultDto> ApproveAsync(int invoiceId, string approvedBy)
    {
        var invoice = await _invoices.GetByIdAsync(invoiceId);
        if (invoice is null) return ApprovalResultDto.NotFound();

        if (invoice.Status != InvoiceStatus.Review)
            return ApprovalResultDto.InvalidStatus(invoice.Status);

        try
        {
            // Generate ZATCA XML for SA invoices that weren't processed at ingest time
            if (invoice.Region == ComplianceRegion.SaudiArabia &&
                string.IsNullOrEmpty(invoice.XmlHash))
            {
                await GenerateZatcaXmlAsync(invoice, invoiceId);
            }

            // Promote status to VALID and record human sign-off
            await _invoices.UpdateStatusAsync(invoiceId, InvoiceStatus.Valid);
            await _invoices.RecordApprovalMetadataAsync(invoiceId, approvedBy);

            // Move source file from review → success
            MoveFileToSuccess(invoice.SourceFile);

            _logger.LogInformation("Invoice {Id} ({Number}) approved as VALID by {User}",
                invoiceId, invoice.InvoiceNumber, approvedBy);

            return ApprovalResultDto.Ok(invoiceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to approve invoice {Id}", invoiceId);
            return ApprovalResultDto.Error(ex.Message);
        }
    }

    private async Task GenerateZatcaXmlAsync(Invoice invoice, int invoiceId)
    {
        var lineItems = await _lineItems.GetByInvoiceIdAsync(invoiceId);
        var prevHash  = await _zatca.GetPreviousHashAsync(invoice.VendorTrn, invoiceId);
        var (xmlContent, xmlHash) = await _zatca.GenerateXmlAsync(invoice, lineItems, prevHash);

        Directory.CreateDirectory(_xmlOutputPath);
        var xmlFileName = $"{Sanitize(invoice.InvoiceNumber)}_{invoiceId}.xml";
        var archivePath = Path.Combine(_xmlOutputPath, xmlFileName);
        await File.WriteAllTextAsync(archivePath, xmlContent);

        Directory.CreateDirectory(_successPath);
        await File.WriteAllTextAsync(Path.Combine(_successPath, xmlFileName), xmlContent);

        var qrCode = _zatca.GenerateQrCode(invoice);
        await _invoices.UpdateComplianceFieldsAsync(invoiceId, archivePath, xmlHash, qrCode, prevHash);

        _logger.LogInformation("ZATCA XML generated on approval: {File}", xmlFileName);
    }

    private void MoveFileToSuccess(string? sourcePath)
    {
        if (string.IsNullOrEmpty(sourcePath)) return;

        var fileName   = Path.GetFileName(sourcePath);
        var reviewFile = Path.Combine(_reviewPath, fileName);

        // Original SourceFile is the inbox path; after processing it lives in the review folder
        var actual = File.Exists(sourcePath) ? sourcePath
                   : File.Exists(reviewFile) ? reviewFile
                   : null;

        if (actual is null)
        {
            _logger.LogWarning("Source file not found for move: {Path}", sourcePath);
            return;
        }

        Directory.CreateDirectory(_successPath);
        var dest = Path.Combine(_successPath, fileName);
        if (File.Exists(dest))
        {
            var ext  = Path.GetExtension(fileName);
            var name = Path.GetFileNameWithoutExtension(fileName);
            dest = Path.Combine(_successPath, $"{name}_{DateTime.UtcNow:yyyyMMddHHmmss}{ext}");
        }

        File.Move(actual, dest);
        _logger.LogInformation("Moved {File} → success/", fileName);
    }

    private static string Sanitize(string name) =>
        name.Replace("/", "-").Replace("\\", "-").Replace(":", "-");
}
