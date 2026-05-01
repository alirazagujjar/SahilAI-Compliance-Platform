using SahilAI.Application.DTOs;

namespace SahilAI.Application.Interfaces;

public interface IInvoiceProcessingService
{
    Task<ProcessingResultDto> ProcessDocumentAsync(string filePath, string region);

    decimal ConfidenceThreshold { get; }
}
