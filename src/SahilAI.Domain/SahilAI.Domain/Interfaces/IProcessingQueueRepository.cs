using SahilAI.Domain.Entities;

namespace SahilAI.Domain.Interfaces;

public interface IProcessingQueueRepository
{
    Task<int> EnqueueAsync(ProcessingQueueItem item);
    Task UpdateAsync(int id, string status, int? invoiceId = null, string? error = null);
}
