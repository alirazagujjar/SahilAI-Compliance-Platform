namespace SahilAI.Application.DTOs;

public sealed record ApprovalResultDto(bool Success, string Message, int? InvoiceId = null)
{
    public static ApprovalResultDto Ok(int id)            => new(true,  "Invoice approved and moved to success.", id);
    public static ApprovalResultDto NotFound()            => new(false, "Invoice not found.");
    public static ApprovalResultDto InvalidStatus(string s) => new(false, $"Cannot approve invoice with status '{s}'. Only REVIEW invoices can be approved.");
    public static ApprovalResultDto Error(string msg)     => new(false, msg);
}
