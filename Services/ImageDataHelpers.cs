using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
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

    public static async Task<(int Width, int Height)> GetImageDimensionsAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(imageBytes.AsBuffer());
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        return ((int)decoder.PixelWidth, (int)decoder.PixelHeight);
    }

    public static async Task<string> EnsureDataUrlAspectRatioAsync(
        string dataUrl,
        int targetWidth,
        int targetHeight,
        CancellationToken cancellationToken = default)
    {
        if (targetWidth <= 0 || targetHeight <= 0)
        {
            return dataUrl;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var (mimeType, base64Data) = ParseDataUrl(dataUrl);
        var imageBytes = Convert.FromBase64String(base64Data);

        using var sourceStream = new InMemoryRandomAccessStream();
        await sourceStream.WriteAsync(imageBytes.AsBuffer());
        sourceStream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(sourceStream);
        var sourceWidth = decoder.PixelWidth;
        var sourceHeight = decoder.PixelHeight;
        if (sourceWidth == 0 || sourceHeight == 0)
        {
            return dataUrl;
        }

        var sourceAspect = (double)sourceWidth / sourceHeight;
        var targetAspect = (double)targetWidth / targetHeight;

        if (Math.Abs(sourceAspect - targetAspect) <= 0.01d)
        {
            return dataUrl;
        }

        cancellationToken.ThrowIfCancellationRequested();

        uint cropWidth;
        uint cropHeight;
        uint cropX;
        uint cropY;

        if (sourceAspect > targetAspect)
        {
            cropHeight = sourceHeight;
            cropWidth = (uint)Math.Max(1, Math.Round(cropHeight * targetAspect));
            cropX = (sourceWidth - cropWidth) / 2;
            cropY = 0;
        }
        else
        {
            cropWidth = sourceWidth;
            cropHeight = (uint)Math.Max(1, Math.Round(cropWidth / targetAspect));
            cropX = 0;
            cropY = (sourceHeight - cropHeight) / 2;
        }

        var transform = new BitmapTransform
        {
            Bounds = new BitmapBounds
            {
                X = cropX,
                Y = cropY,
                Width = Math.Min(cropWidth, sourceWidth - cropX),
                Height = Math.Min(cropHeight, sourceHeight - cropY)
            }
        };

        var croppedBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);

        var encoderId = ResolveEncoderId(mimeType, out var outputMimeType);

        using var outputStream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(encoderId, outputStream);
        encoder.SetSoftwareBitmap(croppedBitmap);
        await encoder.FlushAsync();

        outputStream.Seek(0);
        using var netStream = outputStream.AsStreamForRead();
        using var memoryStream = new MemoryStream();
        await netStream.CopyToAsync(memoryStream, cancellationToken);
        var outputBytes = memoryStream.ToArray();

        return BuildDataUrl(outputMimeType, Convert.ToBase64String(outputBytes));
    }

    private static Guid ResolveEncoderId(string mimeType, out string outputMimeType)
    {
        switch (mimeType.ToLowerInvariant())
        {
            case "image/jpeg":
            case "image/jpg":
                outputMimeType = "image/jpeg";
                return BitmapEncoder.JpegEncoderId;
            case "image/png":
                outputMimeType = "image/png";
                return BitmapEncoder.PngEncoderId;
            default:
                outputMimeType = "image/png";
                return BitmapEncoder.PngEncoderId;
        }
    }
}
