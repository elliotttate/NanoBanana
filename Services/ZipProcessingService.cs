using System.IO.Compression;

namespace NanoBananaProWinUI.Services;

public sealed class ZipProcessingService
{
    public async Task<byte[]> CreateBatchZipAsync(
        IReadOnlyList<BatchGenerationResult> results,
        OutputImageFormat outputImageFormat,
        CancellationToken cancellationToken = default)
    {
        await using var outputMemoryStream = new MemoryStream();
        using (var archive = new ZipArchive(outputMemoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var result in results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativeDirectory = BuildSafeDirectoryPath(result.OriginalRelativePath);
                var baseName = BuildSafeFolderName(result.OriginalName);
                var basePath = string.IsNullOrWhiteSpace(relativeDirectory)
                    ? baseName
                    : $"{relativeDirectory}/{baseName}";

                for (var i = 0; i < result.GeneratedImages.Count; i++)
                {
                    var outputDataUrl = await ImageDataHelpers.ConvertDataUrlForOutputFormatAsync(
                        result.GeneratedImages[i],
                        outputImageFormat,
                        cancellationToken);
                    var (mimeType, base64Data) = ImageDataHelpers.ParseDataUrl(outputDataUrl);
                    var extension = ImageDataHelpers.MimeTypeToExtension(mimeType);
                    var entryPath = $"{basePath}/variation_{i + 1}.{extension}";

                    var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
                    await using var entryStream = entry.Open();
                    var bytes = Convert.FromBase64String(base64Data);
                    await entryStream.WriteAsync(bytes, cancellationToken);
                }
            }
        }

        return outputMemoryStream.ToArray();
    }

    private static string BuildSafeFolderName(string originalName)
    {
        var baseName = Path.GetFileName(originalName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "image";
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            baseName = baseName.Replace(invalidChar, '_');
        }

        return string.IsNullOrWhiteSpace(baseName) ? "image" : baseName.Trim();
    }

    private static string BuildSafeDirectoryPath(string originalRelativePath)
    {
        var directory = Path.GetDirectoryName(originalRelativePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return string.Empty;
        }

        var segments = directory
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(BuildSafeSegment)
            .Where(segment => !string.IsNullOrWhiteSpace(segment));

        return string.Join('/', segments);
    }

    private static string BuildSafeSegment(string segment)
    {
        var safeSegment = segment;
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            safeSegment = safeSegment.Replace(invalidChar, '_');
        }

        return string.IsNullOrWhiteSpace(safeSegment) ? "_" : safeSegment.Trim();
    }
}
