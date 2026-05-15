namespace SahilAI.Domain.Entities;

public class AppUser
{
    public int  Id       { get; set; }
    public int  TenantId { get; set; }
    public int? VendorId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = UserRole.Reviewer;
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class UserRole
{
    public const string Admin    = "Admin";
    public const string Reviewer = "Reviewer";
    public const string Viewer   = "Viewer";
    public const string Vendor   = "Vendor";
}
