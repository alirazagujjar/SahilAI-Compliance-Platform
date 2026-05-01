using SahilAI.Domain.Documents;

namespace SahilAI.Infrastructure.Documents;

public sealed class TextDocumentReader : IDocumentReader
{
    private static readonly HashSet<string> Supported = [".txt", ".csv", ".xml"];

    public bool CanRead(string fileExtension) =>
        Supported.Contains(fileExtension.ToLowerInvariant());

    public async Task<DocumentContent> ReadAsync(string filePath)
    {
        var text = await File.ReadAllTextAsync(filePath);
        return new DocumentContent
        {
            Text          = text,
            MimeType      = "text/plain",
            FileExtension = Path.GetExtension(filePath).ToLowerInvariant(),
            PageCount     = 1
        };
    }
}
