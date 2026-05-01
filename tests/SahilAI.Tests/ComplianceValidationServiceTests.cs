using SahilAI.Application.Services;
using SahilAI.Domain.Entities;

namespace SahilAI.Tests;

public class ComplianceValidationServiceTests
{
    private static Invoice ValidUaeInvoice() => new()
    {
        InvoiceNumber = "INV-2024-001",
        VendorName = "Dubai Freight LLC",
        VendorTrn = "100123456700003",
        InvoiceDate = new DateTime(2024, 1, 15),
        Subtotal = 10000m,
        TaxRate = 0.05m,
        TaxAmount = 500m,
        GrandTotal = 10500m,
        Currency = "AED",
        Region = ComplianceRegion.UAE
    };

    [Fact]
    public void Validate_ValidInvoice_ReturnsNoAnomalies()
    {
        var invoice = ValidUaeInvoice();
        var result = ComplianceValidationService.Validate(invoice);
        Assert.Empty(result);
    }

    [Fact]
    public void Validate_TaxMismatch_FlagsTaxAnomaly()
    {
        var invoice = ValidUaeInvoice();
        invoice.TaxAmount = 600m;
        var result = ComplianceValidationService.Validate(invoice);
        Assert.Contains(result, r => r.StartsWith("TAX_MISMATCH"));
    }

    [Fact]
    public void Validate_GrandTotalMismatch_FlagsTotalAnomaly()
    {
        var invoice = ValidUaeInvoice();
        invoice.GrandTotal = 11000m;
        var result = ComplianceValidationService.Validate(invoice);
        Assert.Contains(result, r => r.StartsWith("TOTAL_MISMATCH"));
    }

    [Fact]
    public void Validate_MissingTrn_FlagsMissingTrn()
    {
        var invoice = ValidUaeInvoice();
        invoice.VendorTrn = string.Empty;
        var result = ComplianceValidationService.Validate(invoice);
        Assert.Contains(result, r => r.StartsWith("MISSING_TRN"));
    }

    [Fact]
    public void Validate_InvalidUaeTrn_FlagsInvalidTrn()
    {
        var invoice = ValidUaeInvoice();
        invoice.VendorTrn = "12345";
        var result = ComplianceValidationService.Validate(invoice);
        Assert.Contains(result, r => r.StartsWith("INVALID_UAE_TRN"));
    }

    [Fact]
    public void Validate_MissingInvoiceNumber_FlagsBlankNumber()
    {
        var invoice = ValidUaeInvoice();
        invoice.InvoiceNumber = string.Empty;
        var result = ComplianceValidationService.Validate(invoice);
        Assert.Contains(result, r => r.StartsWith("MISSING_INVOICE_NUMBER"));
    }

    [Fact]
    public void Validate_SaudiRegion_ValidTrn_ReturnsNoTrnAnomaly()
    {
        var invoice = ValidUaeInvoice();
        invoice.Region = ComplianceRegion.SaudiArabia;
        invoice.VendorTrn = "300123456700003";
        var result = ComplianceValidationService.Validate(invoice);
        Assert.DoesNotContain(result, r => r.StartsWith("INVALID_ZATCA_TRN"));
    }
}
