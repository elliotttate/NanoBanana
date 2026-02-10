using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace NanoBananaProWinUI.Services;

public static class ImageDataHelpers
{
    public static readonly IReadOnlySet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
    };

    public static string InferMimeType(string fileNameOrExtension)
    {
        var ext = Path.GetExtension(fileNameOrExtension);
        if (string.IsNullOrWhiteSpace(ext))
        {
            ext = fileNameOrExtension;
        }

        return ext.ToLowerInvariant() switch
        {
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/png",
        };
    }

    public static string MimeTypeToExtension(string mimeType)
    {
        return mimeType.ToLowerInvariant() switch
        {
            "image/jpeg" => "jpg",
            "image/jpg" => "jpg",
            "image/webp" => "webp",
            _ => "png",
        };
    }

    public static string BuildDataUrl(string mimeType, string base64Data)
    {
        return $"data:{mimeType};base64,{base64Data}";
    }

    public static (string MimeType, string Base64Data) ParseDataUrl(string dataUrl)
    {
        var parts = dataUrl.Split(',', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !parts[0].StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid image data format received.");
        }

        var metadata = parts[0];
        var mimeStart = "data:".Length;
        var mimeEnd = metadata.IndexOf(';');
        if (mimeEnd <= mimeStart)
        {
            throw new InvalidOperationException("Invalid image metadata in generated result.");
        }

        var mimeType = metadata[mimeStart..mimeEnd];
        return (mimeType, parts[1]);
    }

    public static byte[] DataUrlToBytes(string dataUrl, out string mimeType)
    {
        var parsed = ParseDataUrl(dataUrl);
        mimeType = parsed.MimeType;
        return Convert.FromBase64String(parsed.Base64Data);
    }

    public static async Task<BitmapImage> CreateBitmapImageAsync(byte[] imageBytes)
    {
        var image = new BitmapImage();
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(imageBytes.AsBuffer());
        stream.Seek(0);
        await image.SetSourceAsync(stream);
        return image;
    }
}
