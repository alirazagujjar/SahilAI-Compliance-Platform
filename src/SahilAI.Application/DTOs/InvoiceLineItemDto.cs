namespace SahilAI.Application.DTOs;

public record InvoiceLineItemDto(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal
);
