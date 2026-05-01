using SahilAI.Domain.Entities;

namespace SahilAI.Domain.Interfaces;

public interface IInvoiceLineItemRepository
{
    Task InsertBatchAsync(IEnumerable<InvoiceLineItem> items);
    Task<IEnumerable<InvoiceLineItem>> GetByInvoiceIdAsync(int invoiceId);
}
