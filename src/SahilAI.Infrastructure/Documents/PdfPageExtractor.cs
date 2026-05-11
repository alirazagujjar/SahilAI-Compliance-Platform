using System.Text;
using UglyToad.PdfPig;

namespace SahilAI.Infrastructure.Documents;

/// <summary>
/// Extracts per-page text from a PDF stream. Used by the upload pipeline
/// to split a multi-invoice PDF into individual processing units.
/// </summary>
public interface IPdfPageExtractor
{
    /// <summary>
    /// Returns one <see cref="PdfPageContent"/> entry per page in the PDF.
    /// Pages with no selectable text still appear (IsScanned = true).
    /// </summary>
    Task<IReadOnlyList<PdfPageContent>> ExtractPagesAsync(Stream stream);
}

public sealed record PdfPageContent(
    int    PageNumber,
    int    TotalPages,
    string Text,
    bool   IsScanned);

public sealed class PdfPigPageExtractor : IPdfPageExtractor
{
    public Task<IReadOnlyList<PdfPageContent>> ExtractPagesAsync(Stream stream)
    {
        using var pdf   = PdfDocument.Open(stream);
        var pages       = pdf.GetPages().ToList();
        var result      = new List<PdfPageContent>(pages.Count);

        foreach (var page in pages)
        {
            var sb = new StringBuilder();
            foreach (var word in page.GetWords())
            {
                sb.Append(word.Text);
                sb.Append(' ');
            }
            var text = sb.ToString().Trim();
            result.Add(new PdfPageContent(page.Number, pages.Count, text, string.IsNullOrEmpty(text)));
        }

        return Task.FromResult<IReadOnlyList<PdfPageContent>>(result);
    }
}
