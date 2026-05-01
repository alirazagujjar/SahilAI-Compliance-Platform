using SahilAI.Domain.Entities;

namespace SahilAI.Domain.Interfaces;

public interface IAuditLogRepository
{
    Task InsertAsync(AgentAuditLog log);
    Task<IEnumerable<AgentAuditLog>> GetByInvoiceIdAsync(int invoiceId);
}
