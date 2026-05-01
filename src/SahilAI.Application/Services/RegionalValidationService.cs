using SahilAI.Domain.Entities;
using SahilAI.Domain.Interfaces;

namespace SahilAI.Application.Services;

/// DB-driven validation that applies region-specific rules from the ValidationRules table.
public sealed class RegionalValidationService
{
    private const decimal Tolerance = 0.02m;

    private readonly IValidationRulesRepository _rules;
    private readonly IVendorRepository          _vendors;

    public RegionalValidationService(IValidationRulesRepository rules, IVendorRepository vendors)
    {
        _rules   = rules;
        _vendors = vendors;
    }

    public async Task<List<string>> ValidateAsync(Invoice invoice)
    {
        var anomalies = new List<string>();

        // Load rules for this region AND universal rules
        var regionRules = (await _rules.GetByRegionAsync(invoice.Region)).ToList();
        var allRules    = (await _rules.GetByRegionAsync("ALL")).ToList();
        var applicable  = regionRules.Concat(allRules)
                                     .Where(r => r.IsActive)
                                     .ToList();

        if (applicable.Count == 0) return anomalies;

        // Resolve the expected tax rate from rules (prefer region-specific)
        var taxRateRule = regionRules.FirstOrDefault(r => r.ExpectedTaxRate.HasValue);
        var expectedRate = taxRateRule?.ExpectedTaxRate;

        // ── TRN validation ───────────────────────────────────────────────────
        var trnRequired = applicable.Any(r => r.MandatoryTRN &&
                                              r.RuleCode is "MISSING_TRN" or "INVALID_TRN_FORMAT"
                                                         or "INVALID_ZATCA_TRN");
        if (trnRequired)
        {
            if (string.IsNullOrWhiteSpace(invoice.VendorTrn))
                anomalies.Add($"REGIONAL_MISSING_TRN: TRN is mandatory for region {invoice.Region}");
            else if (invoice.VendorTrn.Length != 15 || !invoice.VendorTrn.All(char.IsDigit))
                anomalies.Add($"REGIONAL_INVALID_TRN: '{invoice.VendorTrn}' must be exactly 15 digits " +
                              $"for region {invoice.Region}");
        }

        // ── VAT rate check ───────────────────────────────────────────────────
        if (expectedRate.HasValue && invoice.TaxRate > 0)
        {
            var invoiceRatePct = Math.Round(invoice.TaxRate * 100, 2);
            if (Math.Abs(invoiceRatePct - expectedRate.Value) > 0.01m)
                anomalies.Add(
                    $"REGIONAL_VAT_RATE: Expected {expectedRate.Value}% VAT for {invoice.Region} " +
                    $"but invoice shows {invoiceRatePct}%");
        }

        // ── USA: allow 0% tax / no TRN ───────────────────────────────────────
        if (invoice.Region == "USA")
        {
            // 0% is valid for tax-exempt; skip further tax checks
            if (invoice.TaxRate == 0 && invoice.TaxAmount == 0)
                anomalies.RemoveAll(a => a.StartsWith("TAX_MISMATCH") || a.StartsWith("REGIONAL_VAT"));
        }

        // ── SA: Tax Category Code 'S' check ─────────────────────────────────
        if (invoice.Region == "SA" && applicable.Any(r => r.RuleCode == "ZATCA_UUID_REQUIRED"))
        {
            if (string.IsNullOrWhiteSpace(invoice.ZatcaUuid))
                anomalies.Add("ZATCA_UUID_MISSING: ZATCA Phase 2 requires a UUID on every invoice");
        }

        // ── Bottom-up: Quantity × UnitPrice reconciliation ───────────────────
        // (supplements the line_total check in ComplianceValidationService)
        await ValidateLineItemMathAsync(invoice, anomalies);

        // ── Bilingual cross-reference (AR-EN) ────────────────────────────────
        if (invoice.DocumentLanguage == "AR-EN")
            await ValidateBilingualVendorAsync(invoice, anomalies);

        return anomalies;
    }

    private static async Task ValidateLineItemMathAsync(Invoice invoice, List<string> anomalies)
    {
        await Task.CompletedTask; // kept async for future DB line-item fetch

        if (string.IsNullOrWhiteSpace(invoice.ReasoningLog)) return;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(invoice.ReasoningLog);
            if (!doc.RootElement.TryGetProperty("line_items", out var items)) return;

            decimal computedSubtotal = 0m;
            decimal computedTax      = 0m;
            int     lineCount        = 0;

            foreach (var li in items.EnumerateArray())
            {
                var qty  = li.TryGetProperty("quantity",   out var q) && q.TryGetDecimal(out var qv) ? qv : 0m;
                var up   = li.TryGetProperty("unit_price", out var u) && u.TryGetDecimal(out var uv) ? uv : 0m;
                var lt   = li.TryGetProperty("line_total", out var t) && t.TryGetDecimal(out var tv) ? tv : qty * up;

                computedSubtotal += lt;
                lineCount++;
            }

            if (lineCount == 0) return;

            computedSubtotal = Math.Round(computedSubtotal, 2);
            computedTax      = Math.Round(computedSubtotal * invoice.TaxRate, 2);
            var headerSub    = Math.Round(invoice.Subtotal, 2);

            if (Math.Abs(computedSubtotal - headerSub) > Tolerance)
                anomalies.Add(
                    $"MATH_MISMATCH_LINE_ITEMS: Bottom-up sum of {lineCount} items ({computedSubtotal}) " +
                    $"does not match header subtotal ({headerSub})");

            // Tax category cross-check
            var headerTax = Math.Round(invoice.TaxAmount, 2);
            if (invoice.TaxRate > 0 && Math.Abs(computedTax - headerTax) > Tolerance)
                anomalies.Add(
                    $"TAX_CATEGORY_MISMATCH: Expected tax {computedTax} " +
                    $"({invoice.TaxRate:P0} of {computedSubtotal}) but header shows {headerTax}");
        }
        catch (System.Text.Json.JsonException) { /* malformed log — handled elsewhere */ }
    }

    private async Task ValidateBilingualVendorAsync(Invoice invoice, List<string> anomalies)
    {
        if (string.IsNullOrWhiteSpace(invoice.VendorTrn)) return;

        var knownVendor = await _vendors.GetByTrnAsync(invoice.VendorTrn);
        if (knownVendor is null) return; // first time we see this vendor — nothing to cross-check

        var storedName  = knownVendor.Name?.Trim();
        var invoiceName = invoice.VendorName?.Trim();

        if (!string.IsNullOrEmpty(storedName) &&
            !string.IsNullOrEmpty(invoiceName) &&
            !string.Equals(storedName, invoiceName, StringComparison.OrdinalIgnoreCase))
        {
            anomalies.Add(
                $"BILINGUAL_VENDOR_MISMATCH: Invoice vendor '{invoiceName}' " +
                $"does not match registered name '{storedName}' for TRN {invoice.VendorTrn}");
        }
    }
}
