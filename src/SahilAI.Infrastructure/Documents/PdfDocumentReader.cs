using System.Text;
using SahilAI.Domain.Documents;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace SahilAI.Infrastructure.Documents;

public sealed class PdfDocumentReader : IDocumentReader
{
    public bool CanRead(string fileExtension) =>
        fileExtension.Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<DocumentContent> ReadAsync(string filePath)
    {
        using var pdf = PdfDocument.Open(filePath);

        var sb       = new StringBuilder();
        var pages    = pdf.GetPages().ToList();
        var hasText  = false;

        foreach (var page in pages)
        {
            var pageText = string.Join(" ", page.GetWords().Select(w => w.Text));
            if (!string.IsNullOrWhiteSpace(pageText))
            {
                sb.AppendLine($"[Page {page.Number}]");
                sb.AppendLine(pageText);
                sb.AppendLine();
                hasText = true;
            }
        }

        // Scanned PDF — no selectable text found
        if (!hasText)
        {
            return Task.FromResult(new DocumentContent
            {
                Text          = null,
                MimeType      = "application/pdf",
                FileExtension = ".pdf",
                PageCount     = pages.Count,
                IsScannedPdf  = true
            });
        }

        return Task.FromResult(new DocumentContent
        {
            Text          = sb.ToString(),
            MimeType      = "application/pdf",
            FileExtension = ".pdf",
            PageCount     = pages.Count,
            IsScannedPdf  = false
        });
    }
}
