using SahilAI.Domain.Documents;
using SahilAI.Domain.Entities;

namespace SahilAI.Domain.Interfaces;

public interface IComplianceAgent
{
    Task<Invoice?> ExtractAndValidateAsync(DocumentContent document, string sourceFile, string region);
}
