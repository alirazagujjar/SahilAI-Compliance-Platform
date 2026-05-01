using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using SahilAI.Application.Interfaces;
using SahilAI.Domain.Entities;
using SahilAI.Domain.Interfaces;

namespace SahilAI.Application.Services;

public sealed class ZatcaComplianceService : IZatcaComplianceService
{
    // ZATCA-mandated genesis hash — used as PIH when no previous invoice exists.
    // This is Base64(SHA-256("0")) per ZATCA Phase 2 technical specs.
    private const string GenesisHash = "NwZlOopR4nCPVvQ2ubidbc+fBcMVDDjzFMCaFiUVnFI=";

    private static readonly XNamespace Ubl  = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace Cac  = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace Cbc  = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace Ext  = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";

    private readonly IInvoiceRepository _invoices;

    public ZatcaComplianceService(IInvoiceRepository invoices)
    {
        _invoices = invoices;
    }

    public async Task<string> GetPreviousHashAsync(string vendorTrn, int currentInvoiceId)
    {
        var hash = await _invoices.GetPreviousXmlHashAsync(vendorTrn, currentInvoiceId);
        return hash ?? GenesisHash;
    }

    public async Task<(string XmlContent, string XmlHash)> GenerateXmlAsync(
        Invoice invoice,
        IEnumerable<InvoiceLineItem> lineItems,
        string previousHash)
    {
        var items   = lineItems.ToList();
        var currency = invoice.Currency ?? "SAR";
        var uuid    = invoice.ZatcaUuid ?? Guid.NewGuid().ToString("D").ToUpperInvariant();

        var xml = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ubl + "Invoice",
                new XAttribute(XNamespace.Xmlns + "cac", Cac.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "cbc", Cbc.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "ext", Ext.NamespaceName),

                // ── Mandatory header fields ──────────────────────────────────
                new XElement(Cbc + "ProfileID",        "reporting:1.0"),
                new XElement(Cbc + "ID",               invoice.InvoiceNumber),
                new XElement(Cbc + "UUID",             uuid),
                new XElement(Cbc + "IssueDate",        invoice.InvoiceDate.ToString("yyyy-MM-dd")),
                new XElement(Cbc + "IssueTime",        "00:00:00"),
                new XElement(Cbc + "InvoiceTypeCode",
                    new XAttribute("listID", "0211010000100001"), "388"),
                new XElement(Cbc + "DocumentCurrencyCode", currency),
                new XElement(Cbc + "TaxCurrencyCode",      currency),

                // ── PIH reference ────────────────────────────────────────────
                PihReference(previousHash),

                // ── Supplier ─────────────────────────────────────────────────
                SupplierParty(invoice),

                // ── Tax total ────────────────────────────────────────────────
                TaxTotal(invoice, currency),

                // ── Monetary totals ──────────────────────────────────────────
                LegalMonetaryTotal(invoice, currency),

                // ── Line items ───────────────────────────────────────────────
                items.Select((li, i) => InvoiceLine(li, i + 1, currency))
            )
        );

        var xmlContent = xml.ToString(SaveOptions.None);
        var xmlHash    = ComputeHash(xmlContent);

        return (xmlContent, xmlHash);
    }

    public string GenerateQrCode(Invoice invoice)
    {
        // ZATCA TLV: Tag(1 byte) + Length(1 byte) + Value(N bytes), then Base64 the whole buffer.
        var timestamp = invoice.InvoiceDate.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var buffer = new List<byte>();
        AppendTlv(buffer, 1, invoice.VendorName);
        AppendTlv(buffer, 2, invoice.VendorTrn);
        AppendTlv(buffer, 3, timestamp);
        AppendTlv(buffer, 4, invoice.GrandTotal.ToString("F2"));
        AppendTlv(buffer, 5, invoice.TaxAmount.ToString("F2"));

        return Convert.ToBase64String(buffer.ToArray());
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static XElement PihReference(string previousHash) =>
        new(Cac + "AdditionalDocumentReference",
            new XElement(Cbc + "ID", "PIH"),
            new XElement(Cac + "Attachment",
                new XElement(Cbc + "EmbeddedDocumentBinaryObject",
                    new XAttribute("mimeCode", "text/plain"),
                    previousHash)));

    private static XElement SupplierParty(Invoice invoice) =>
        new(Cac + "AccountingSupplierParty",
            new XElement(Cac + "Party",
                new XElement(Cac + "PostalAddress",
                    new XElement(Cac + "Country",
                        new XElement(Cbc + "IdentificationCode",
                            invoice.Region == "SA" ? "SA" : "AE"))),
                new XElement(Cac + "PartyTaxScheme",
                    new XElement(Cbc + "CompanyID", invoice.VendorTrn),
                    new XElement(Cac + "TaxScheme",
                        new XElement(Cbc + "ID", "VAT"))),
                new XElement(Cac + "PartyLegalEntity",
                    new XElement(Cbc + "RegistrationName", invoice.VendorName))));

    private static XElement TaxTotal(Invoice invoice, string currency)
    {
        var taxPercent = invoice.TaxRate > 0 ? invoice.TaxRate * 100 : 15m;

        return new XElement(Cac + "TaxTotal",
            new XElement(Cbc + "TaxAmount",
                new XAttribute("currencyID", currency),
                invoice.TaxAmount.ToString("F2")),
            new XElement(Cac + "TaxSubtotal",
                new XElement(Cbc + "TaxableAmount",
                    new XAttribute("currencyID", currency),
                    invoice.Subtotal.ToString("F2")),
                new XElement(Cbc + "TaxAmount",
                    new XAttribute("currencyID", currency),
                    invoice.TaxAmount.ToString("F2")),
                new XElement(Cac + "TaxCategory",
                    new XElement(Cbc + "ID", "S"),
                    new XElement(Cbc + "Percent", taxPercent.ToString("F2")),
                    new XElement(Cac + "TaxScheme",
                        new XElement(Cbc + "ID", "VAT")))));
    }

    private static XElement LegalMonetaryTotal(Invoice invoice, string currency) =>
        new(Cac + "LegalMonetaryTotal",
            new XElement(Cbc + "LineExtensionAmount",
                new XAttribute("currencyID", currency), invoice.Subtotal.ToString("F2")),
            new XElement(Cbc + "TaxExclusiveAmount",
                new XAttribute("currencyID", currency), invoice.Subtotal.ToString("F2")),
            new XElement(Cbc + "TaxInclusiveAmount",
                new XAttribute("currencyID", currency), invoice.GrandTotal.ToString("F2")),
            new XElement(Cbc + "PayableAmount",
                new XAttribute("currencyID", currency), invoice.GrandTotal.ToString("F2")));

    private static XElement InvoiceLine(InvoiceLineItem li, int lineNum, string currency) =>
        new(Cac + "InvoiceLine",
            new XElement(Cbc + "ID",       lineNum),
            new XElement(Cbc + "InvoicedQuantity",
                new XAttribute("unitCode", "PCE"), li.Quantity.ToString("F4")),
            new XElement(Cbc + "LineExtensionAmount",
                new XAttribute("currencyID", currency), li.LineTotal.ToString("F2")),
            new XElement(Cac + "Item",
                new XElement(Cbc + "Name", li.Description)),
            new XElement(Cac + "Price",
                new XElement(Cbc + "PriceAmount",
                    new XAttribute("currencyID", currency), li.UnitPrice.ToString("F2"))));

    private static void AppendTlv(List<byte> buffer, byte tag, string value)
    {
        var valueBytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        buffer.Add(tag);
        buffer.Add((byte)Math.Min(valueBytes.Length, 255));
        buffer.AddRange(valueBytes.Take(255));
    }

    private static string ComputeHash(string xmlContent)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(xmlContent));
        return Convert.ToBase64String(bytes);
    }
}
