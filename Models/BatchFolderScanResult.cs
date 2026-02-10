namespace NanoBananaProWinUI.Models;

public sealed class BatchFolderScanResult
{
    public required string SourceFolderPath { get; init; }

    public required string OutputFolderPath { get; init; }

    public required IReadOnlyList<BatchFileItem> Files { get; init; }

    public required int TotalCount { get; init; }

    public required int PendingCount { get; init; }

    public required int ProcessedCount { get; init; }
}
