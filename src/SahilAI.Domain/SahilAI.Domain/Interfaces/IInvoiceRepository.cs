using SahilAI.Domain.Entities;

namespace SahilAI.Domain.Interfaces;

public interface IInvoiceRepository
{
    Task<int> InsertAsync(Invoice invoice);
    Task<Invoice?> GetByInvoiceNumberAsync(string invoiceNumber);
    Task<IEnumerable<Invoice>> GetFlaggedAsync();
    Task UpdateStatusAsync(int id, string status);

    /// Returns REVIEW invoices where a human has set IsApproved = 1 in the DB.
    Task<IEnumerable<Invoice>> GetPendingApprovalAsync();

    /// Stamps status = APPROVED and records who/when processed the approval.
    Task MarkApprovedAsync(int id, string processedBy);

    /// Returns the XmlHash of the most recent VALID/APPROVED invoice for this vendor (for PIH chain).
    Task<string?> GetPreviousXmlHashAsync(string vendorTrn, int excludeInvoiceId);

    /// Updates XmlPath, XmlHash, QrCodeBase64 and PreviousInvoiceHash after ZATCA XML generation.
    Task UpdateComplianceFieldsAsync(int id, string? xmlPath, string? xmlHash, string? qrCode, string? previousHash);

    Task<Invoice?> GetByIdAsync(int id);
    Task<IEnumerable<Invoice>> GetReviewQueueAsync();

    /// Sets IsApproved=1, ApprovedAt, ApprovedBy without changing Status (used by web approval flow).
    Task RecordApprovalMetadataAsync(int id, string approvedBy);

    /// Returns invoice counts grouped by status — used by the dashboard.
    Task<IReadOnlyDictionary<string, int>> GetStatusCountsAsync();

    /// Returns VALID + APPROVED invoices ordered most-recent-first.
    Task<IEnumerable<Invoice>> GetProcessedAsync();

    /// Returns NOT_SUPPORTED_FORMAT invoices (files the LLM could not process).
    Task<IEnumerable<Invoice>> GetNotSupportedAsync();

    /// Patches the operator-corrected core fields before approval.
    Task UpdateCoreFieldsAsync(int id, string invoiceNumber, string vendorName, string vendorTrn,
        DateTime invoiceDate, decimal subtotal, decimal taxAmount, decimal grandTotal,
        decimal taxRate, string currency);
}
