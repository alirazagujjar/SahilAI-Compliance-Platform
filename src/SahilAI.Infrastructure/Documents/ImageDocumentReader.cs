using SahilAI.Domain.Documents;

namespace SahilAI.Infrastructure.Documents;

public sealed class ImageDocumentReader : IDocumentReader
{
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"]  = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"]  = "image/png",
        [".bmp"]  = "image/bmp",
        [".tiff"] = "image/tiff",
        [".tif"]  = "image/tiff",
        [".webp"] = "image/webp"
    };

    public bool CanRead(string fileExtension) =>
        MimeTypes.ContainsKey(fileExtension.ToLowerInvariant());

    public async Task<DocumentContent> ReadAsync(string filePath)
    {
        var ext      = Path.GetExtension(filePath).ToLowerInvariant();
        var bytes    = await File.ReadAllBytesAsync(filePath);
        var mimeType = MimeTypes.GetValueOrDefault(ext, "image/jpeg");

        return new DocumentContent
        {
            ImageBytes    = bytes,
            MimeType      = mimeType,
            FileExtension = ext,
            PageCount     = 1
        };
    }
}
