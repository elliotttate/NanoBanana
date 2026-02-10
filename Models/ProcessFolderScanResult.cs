namespace NanoBananaProWinUI.Models;

public sealed class ProcessFolderScanResult
{
    public required string ProcessedFolderPath { get; init; }

    public required string SourceFolderPath { get; init; }

    public required string SelectionOutputFolderPath { get; init; }

    public required IReadOnlyList<ProcessReviewItem> Items { get; init; }

    public required int TotalCount { get; init; }

    public required int ReviewedCount { get; init; }

    public required int PendingCount { get; init; }
}
