namespace SahilAI.Domain.Entities;

public class ValidationRule
{
    public int Id { get; set; }
    public string Region { get; set; } = string.Empty;
    public string RuleCode { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string Severity { get; set; } = "ERROR";
    public string? CountryCode { get; set; }
    public decimal? ExpectedTaxRate { get; set; }
    public bool MandatoryTRN { get; set; } = true;
}
