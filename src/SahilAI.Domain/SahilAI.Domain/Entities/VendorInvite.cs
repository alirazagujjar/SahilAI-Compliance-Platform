namespace SahilAI.Domain.Entities;

public class VendorInvite
{
    public int      Id          { get; set; }
    public string   Email       { get; set; } = string.Empty;
    public string   Token       { get; set; } = string.Empty;
    public int      TenantId    { get; set; } = 1;
    public string?  CompanyName { get; set; }
    public bool     IsUsed      { get; set; }
    public DateTime ExpiresAt   { get; set; }
    public int      InvitedById { get; set; }
    public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
}
