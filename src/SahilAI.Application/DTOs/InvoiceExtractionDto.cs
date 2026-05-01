namespace SahilAI.Application.DTOs;

public record InvoiceExtractionDto(
    string InvoiceNumber,
    string VendorName,
    string VendorTrn,
    DateTime InvoiceDate,
    decimal Subtotal,
    decimal TaxAmount,
    decimal GrandTotal,
    decimal TaxRate,
    string Currency,
    List<InvoiceLineItemDto> LineItems
);
