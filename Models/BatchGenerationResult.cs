namespace NanoBananaProWinUI.Models;

public sealed class BatchGenerationResult
{
    public required string OriginalName { get; init; }

    public required string OriginalRelativePath { get; init; }

    public required IReadOnlyList<string> GeneratedImages { get; init; }
}
