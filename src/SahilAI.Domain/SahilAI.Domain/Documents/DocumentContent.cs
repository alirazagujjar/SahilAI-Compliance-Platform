namespace SahilAI.Domain.Documents;

public sealed class DocumentContent
{
    public string? Text { get; init; }
    public byte[]? ImageBytes { get; init; }
    public string MimeType { get; init; } = "text/plain";
    public string FileExtension { get; init; } = ".txt";
    public int PageCount { get; init; } = 1;
    public bool IsScannedPdf { get; init; }

    public bool HasText  => !string.IsNullOrWhiteSpace(Text);
    public bool HasImage => ImageBytes is { Length: > 0 };

    public string FileType => (FileExtension.ToLowerInvariant(), IsScannedPdf) switch
    {
        (".pdf", true)  => "SCANNED_PDF",
        (".pdf", false) => "PDF",
        (".txt", _) or (".csv", _) or (".xml", _) => "TXT",
        _ when HasImage => "IMAGE",
        _ => "TXT"
    };
}
