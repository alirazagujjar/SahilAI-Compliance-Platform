using SahilAI.Domain.Documents;

namespace SahilAI.Infrastructure.Documents;

public sealed class DocumentReaderFactory : IDocumentReaderFactory
{
    private readonly IReadOnlyList<IDocumentReader> _readers;

    public DocumentReaderFactory()
    {
        _readers =
        [
            new PdfDocumentReader(),
            new ImageDocumentReader(),
            new TextDocumentReader()   // fallback last
        ];
    }

    public static readonly HashSet<string> SupportedExtensions =
    [
        ".txt", ".csv", ".xml",
        ".pdf",
        ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".tif", ".webp"
    ];

    public IDocumentReader GetReader(string filePath)
    {
        var ext    = Path.GetExtension(filePath).ToLowerInvariant();
        var reader = _readers.FirstOrDefault(r => r.CanRead(ext));

        if (reader is null)
            throw new NotSupportedException(
                $"No reader available for extension '{ext}'. Supported: {string.Join(", ", SupportedExtensions)}");

        return reader;
    }
}
