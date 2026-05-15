namespace SahilAI.Domain.Entities;

public class Invoice
{
    public int Id       { get; set; }
    public int TenantId { get; set; } = 1;
    public string InvoiceNumber { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
    public string VendorTrn { get; set; } = string.Empty;
    public DateTime InvoiceDate { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal TaxRate { get; set; }
    public string Currency { get; set; } = "AED";
    public string Status { get; set; } = InvoiceStatus.Pending;
    public string Region { get; set; } = ComplianceRegion.UAE;
    public string? ZatcaUuid { get; set; }
    public string? PreviousInvoiceHash { get; set; }
    public string? XmlPath { get; set; }
    public string? XmlHash { get; set; }
    public string? QrCodeBase64 { get; set; }
    public string? AnomalyFlags { get; set; }
    public decimal ConfidenceScore { get; set; }
    public string DocumentLanguage { get; set; } = "EN";
    public string FileType { get; set; } = "TXT";
    public string ReasoningLog { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string SourceFile { get; set; } = string.Empty;

    // HITL approval
    public bool IsApproved { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedBy { get; set; }
}
