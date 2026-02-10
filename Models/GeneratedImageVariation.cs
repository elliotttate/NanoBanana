using Microsoft.UI.Xaml.Media.Imaging;

namespace NanoBananaProWinUI.Models;

public sealed class GeneratedImageVariation
{
    public required int Index { get; init; }

    public required string DataUrl { get; init; }

    public required BitmapImage PreviewImage { get; init; }
}
