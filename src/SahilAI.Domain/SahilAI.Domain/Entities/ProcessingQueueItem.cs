namespace SahilAI.Domain.Entities;

public class ProcessingQueueItem
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? FileHash { get; set; }
    public string Region { get; set; } = ComplianceRegion.UAE;
    public string FileType { get; set; } = "TXT";
    public string QueueStatus { get; set; } = "QUEUED";
    public short RetryCount { get; set; }
    public int? InvoiceId { get; set; }
    public string? ErrorDetail { get; set; }
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
}

public static class QueueStatus
{
    public const string Queued         = "QUEUED";
    public const string Processing     = "PROCESSING";
    public const string Done           = "DONE";
    public const string Failed         = "FAILED";
    public const string InvalidFormat  = "INVALID_FORMAT";
}
