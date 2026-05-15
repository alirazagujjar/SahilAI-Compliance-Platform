namespace SahilAI.Domain.Entities;

public class Vendor
{
    public int Id { get; set; }
    public int TenantId { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public string TaxRegNumber { get; set; } = string.Empty;
    public string Region { get; set; } = ComplianceRegion.UAE;
    public bool IsVerified { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
