using System.IO.Compression;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage;

namespace NanoBananaProWinUI.Services;

public sealed class ZipProcessingService
{
    public async Task<IReadOnlyList<BatchFileItem>> ExtractImagesFromZipAsync(StorageFile zipFile, CancellationToken cancellationToken = default)
    {
        var images = new List<BatchFileItem>();

        using var zipReadStream = await zipFile.OpenReadAsync();
        using var netReadStream = zipReadStream.AsStreamForRead();
        using var archive = new ZipArchive(netReadStream, ZipArchiveMode.Read, leaveOpen: false);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                continue;
            }

            var normalizedPath = entry.FullName.Replace('\\', '/');
            if (normalizedPath.Contains("__MACOSX", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Path.GetFileName(normalizedPath).StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }

            var extension = Path.GetExtension(entry.Name);
            if (!ImageDataHelpers.SupportedExtensions.Contains(extension))
            {
                continue;
            }

            await using var entryStream = entry.Open();
            using var memoryStream = new MemoryStream();
            await entryStream.CopyToAsync(memoryStream, cancellationToken);
            var bytes = memoryStream.ToArray();

            images.Add(new BatchFileItem
            {
                Name = normalizedPath,
                Base64Data = Convert.ToBase64String(bytes),
                MimeType = ImageDataHelpers.InferMimeType(entry.Name)
            });
        }

        return images;
    }

    public async Task<byte[]> CreateBatchZipAsync(IReadOnlyList<BatchGenerationResult> results, CancellationToken cancellationToken = default)
    {
        await using var outputMemoryStream = new MemoryStream();
        using (var archive = new ZipArchive(outputMemoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var result in results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var folderName = BuildSafeFolderName(result.OriginalName);
                for (var i = 0; i < result.GeneratedImages.Count; i++)
                {
                    var (mimeType, base64Data) = ImageDataHelpers.ParseDataUrl(result.GeneratedImages[i]);
                    var extension = ImageDataHelpers.MimeTypeToExtension(mimeType);
                    var entryPath = $"{folderName}/variation_{i + 1}.{extension}";

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
        var baseName = Path.GetFileNameWithoutExtension(originalName);
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
}
