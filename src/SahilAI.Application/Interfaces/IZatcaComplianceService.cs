using SahilAI.Domain.Entities;

namespace SahilAI.Application.Interfaces;

public interface IZatcaComplianceService
{
    /// Builds a ZATCA-compliant UBL 2.1 XML string and returns it with its SHA-256 hash.
    Task<(string XmlContent, string XmlHash)> GenerateXmlAsync(
        Invoice invoice,
        IEnumerable<InvoiceLineItem> lineItems,
        string previousHash);

    /// Gets the XmlHash of the previous successful invoice for this vendor.
    /// Returns the ZATCA genesis hash when no previous invoice exists.
    Task<string> GetPreviousHashAsync(string vendorTrn, int currentInvoiceId);

    /// Builds a Base64-encoded TLV QR code string (ZATCA TLV format).
    string GenerateQrCode(Invoice invoice);
}
