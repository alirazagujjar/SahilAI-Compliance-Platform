using System.Text.Json;
using SahilAI.Domain.Entities;

namespace SahilAI.Application.Services;

public static class ComplianceValidationService
{
    private const decimal Tolerance = 0.01m;

    public static List<string> Validate(Invoice invoice)
    {
        var anomalies = new List<string>();

        // Header-level checks
        var expectedTax = Math.Round(invoice.Subtotal * invoice.TaxRate, 2);
        if (Math.Abs(expectedTax - invoice.TaxAmount) > Tolerance)
            anomalies.Add($"TAX_MISMATCH: Expected {expectedTax} but found {invoice.TaxAmount}");

        var expectedTotal = Math.Round(invoice.Subtotal + invoice.TaxAmount, 2);
        if (Math.Abs(expectedTotal - invoice.GrandTotal) > Tolerance)
            anomalies.Add($"TOTAL_MISMATCH: Expected {expectedTotal} but found {invoice.GrandTotal}");

        if (string.IsNullOrWhiteSpace(invoice.VendorTrn))
            anomalies.Add("MISSING_TRN: Vendor Tax Registration Number is absent");

        if (invoice.Region == ComplianceRegion.UAE && !IsValidUaeTrn(invoice.VendorTrn))
            anomalies.Add($"INVALID_UAE_TRN: '{invoice.VendorTrn}' does not meet 15-digit TRN format");

        if (invoice.Region == ComplianceRegion.SaudiArabia && !IsValidZatcaTrn(invoice.VendorTrn))
            anomalies.Add($"INVALID_ZATCA_TRN: '{invoice.VendorTrn}' does not meet ZATCA format");

        if (string.IsNullOrWhiteSpace(invoice.InvoiceNumber))
            anomalies.Add("MISSING_INVOICE_NUMBER: Invoice number is blank");

        // Line-item reconciliation: sum of line_total values must match the header subtotal
        ValidateLineItemReconciliation(invoice, anomalies);

        return anomalies;
    }

    private static void ValidateLineItemReconciliation(Invoice invoice, List<string> anomalies)
    {
        if (string.IsNullOrWhiteSpace(invoice.ReasoningLog)) return;

        try
        {
            using var doc = JsonDocument.Parse(invoice.ReasoningLog);
            if (!doc.RootElement.TryGetProperty("line_items", out var lineItemsEl)) return;

            decimal lineSum = 0m;
            int count = 0;

            foreach (var li in lineItemsEl.EnumerateArray())
            {
                if (li.TryGetProperty("line_total", out var lt) && lt.TryGetDecimal(out var v))
                {
                    lineSum += v;
                    count++;
                }
            }

            if (count == 0) return;

            lineSum = Math.Round(lineSum, 2);
            var headerSubtotal = Math.Round(invoice.Subtotal, 2);

            if (Math.Abs(lineSum - headerSubtotal) > Tolerance)
                anomalies.Add(
                    $"LINE_ITEM_SUBTOTAL_MISMATCH: Sum of {count} line items ({lineSum}) " +
                    $"does not match header subtotal ({headerSubtotal})");
        }
        catch (JsonException)
        {
            // ReasoningLog is malformed — skip silently; text extraction issues handled elsewhere
        }
    }

    private static bool IsValidUaeTrn(string trn) =>
        !string.IsNullOrWhiteSpace(trn) && trn.Replace("-", "").Length == 15 && trn.All(c => char.IsDigit(c) || c == '-');

    private static bool IsValidZatcaTrn(string trn) =>
        !string.IsNullOrWhiteSpace(trn) && trn.Length == 15 && trn.All(char.IsDigit);
}
