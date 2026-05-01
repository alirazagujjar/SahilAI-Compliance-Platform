namespace SahilAI.Application.DTOs;

public record ProcessingResultDto(
    bool Success,
    string Status,
    int? InvoiceId,
    string? Message,
    List<string> Anomalies,
    decimal ConfidenceScore = 0
);
