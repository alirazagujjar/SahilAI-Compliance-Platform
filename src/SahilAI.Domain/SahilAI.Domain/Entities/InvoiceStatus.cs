namespace SahilAI.Domain.Entities;

public static class InvoiceStatus
{
    public const string Pending   = "PENDING";
    public const string Valid     = "VALID";
    public const string Flagged   = "FLAGGED";
    public const string Duplicate = "DUPLICATE";
    public const string Review             = "REVIEW";
    public const string Approved           = "APPROVED";
    public const string NotSupportedFormat = "NOT_SUPPORTED_FORMAT";
    public const string Error              = "ERROR";
}
