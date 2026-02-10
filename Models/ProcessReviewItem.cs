namespace NanoBananaProWinUI.Models;

public sealed class ProcessReviewItem
{
    public required string RelativeSourcePath { get; init; }

    public required string OriginalFilePath { get; init; }

    public required string VariationFolderPath { get; init; }

    public required IReadOnlyList<string> VariationFilePaths { get; set; }

    public bool IsReviewed { get; set; }

    public int SelectedVariationIndex { get; set; }

    public string Notes { get; set; } = string.Empty;

    public bool Transparency { get; set; }

    public string SelectedOutputRelativePath { get; set; } = string.Empty;
}
