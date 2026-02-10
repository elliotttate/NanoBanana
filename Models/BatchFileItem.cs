namespace NanoBananaProWinUI.Models;

public sealed class BatchFileItem
{
    public required string Name { get; init; }

    public required string Base64Data { get; init; }

    public required string MimeType { get; init; }

    public string ShortType => MimeType.Split('/').LastOrDefault() ?? "img";
}
