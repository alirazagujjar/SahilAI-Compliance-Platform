namespace SahilAI.Domain.Documents;

public interface IDocumentReader
{
    bool CanRead(string fileExtension);
    Task<DocumentContent> ReadAsync(string filePath);
}
