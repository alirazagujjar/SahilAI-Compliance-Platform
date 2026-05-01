namespace SahilAI.Domain.Documents;

public interface IDocumentReaderFactory
{
    IDocumentReader GetReader(string filePath);
}
