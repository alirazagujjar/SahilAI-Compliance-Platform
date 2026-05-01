namespace SahilAI.Domain.Entities;

public class AgentAuditLog
{
    public int Id { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string InputSummary { get; set; } = string.Empty;
    public string OutputJson { get; set; } = "{}";
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public long DurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? InvoiceId { get; set; }
}
