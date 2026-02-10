using System.Text;
using Windows.Storage;

namespace NanoBananaProWinUI.Services;

public sealed class ProcessReviewService
{
    private static readonly string SettingsDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NanoBananaProWinUI");
    private static readonly string ReviewIndexPath = Path.Combine(SettingsDirectoryPath, "process_mode_index.db");
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, FolderReviewRecord> _folderRecords = new(StringComparer.OrdinalIgnoreCase);

    public ProcessReviewService()
    {
        LoadIndexFromDisk();
    }

    public async Task<ProcessFolderScanResult> ScanProcessedFolderAsync(string processedFolderPath, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => ScanProcessedFolder(processedFolderPath, cancellationToken), cancellationToken);
    }

    public async Task<string> SaveSelectionAsync(
        string processedFolderPath,
        string selectionOutputFolderPath,
        ProcessReviewItem item,
        int selectedVariationIndex,
        string notes,
        bool transparency,
        CancellationToken cancellationToken = default)
    {
        if (selectedVariationIndex < 1 || selectedVariationIndex > item.VariationFilePaths.Count)
        {
            throw new InvalidOperationException("Selected variation is not available.");
        }

        var selectedVariationPath = item.VariationFilePaths[selectedVariationIndex - 1];
        var selectedExtension = Path.GetExtension(selectedVariationPath);
        if (string.IsNullOrWhiteSpace(selectedExtension))
        {
            selectedExtension = ".png";
        }

        var destinationPath = BuildSelectionOutputPath(
            selectionOutputFolderPath,
            item.RelativeSourcePath,
            selectedExtension);

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        File.Copy(selectedVariationPath, destinationPath, overwrite: true);
        await WriteMetadataAsync(destinationPath, notes, transparency);

        var outputRelativePath = Path.GetRelativePath(selectionOutputFolderPath, destinationPath).Replace('\\', '/');
        SaveReviewRecord(
            processedFolderPath,
            item.RelativeSourcePath,
            selectedVariationIndex,
            notes,
            transparency,
            outputRelativePath);

        return destinationPath;
    }

    public async Task<IReadOnlyList<string>> OverwriteVariationsAsync(
        ProcessReviewItem item,
        IReadOnlyList<string> generatedImages,
        CancellationToken cancellationToken = default)
    {
        if (generatedImages.Count == 0)
        {
            return item.VariationFilePaths;
        }

        Directory.CreateDirectory(item.VariationFolderPath);

        foreach (var existingFilePath in Directory.EnumerateFiles(item.VariationFolderPath, "variation_*"))
        {
            if (!ImageDataHelpers.SupportedExtensions.Contains(Path.GetExtension(existingFilePath)))
            {
                continue;
            }

            File.Delete(existingFilePath);
        }

        var variationPaths = new List<string>(generatedImages.Count);
        for (var index = 0; index < generatedImages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (mimeType, base64Data) = ImageDataHelpers.ParseDataUrl(generatedImages[index]);
            var extension = ImageDataHelpers.MimeTypeToExtension(mimeType);
            var variationFileName = $"variation_{index + 1}.{extension}";
            var variationAbsolutePath = Path.Combine(item.VariationFolderPath, variationFileName);
            var imageBytes = Convert.FromBase64String(base64Data);
            await File.WriteAllBytesAsync(variationAbsolutePath, imageBytes, cancellationToken);
            variationPaths.Add(variationAbsolutePath);
        }

        variationPaths.Sort(CompareVariationPaths);
        return variationPaths;
    }

    public void ClearReviewRecord(string processedFolderPath, string relativeSourcePath)
    {
        var normalizedProcessedFolder = NormalizeAbsolutePath(processedFolderPath);
        lock (_syncRoot)
        {
            if (!_folderRecords.TryGetValue(normalizedProcessedFolder, out var folderRecord))
            {
                return;
            }

            if (folderRecord.Items.Remove(relativeSourcePath))
            {
                SaveIndexToDisk();
            }
        }
    }

    private ProcessFolderScanResult ScanProcessedFolder(string processedFolderPath, CancellationToken cancellationToken)
    {
        var normalizedProcessedFolder = NormalizeAbsolutePath(processedFolderPath);
        if (!Directory.Exists(normalizedProcessedFolder))
        {
            throw new DirectoryNotFoundException($"Processed folder does not exist: {normalizedProcessedFolder}");
        }

        var sourceFolderPath = ResolveSourceFolderPath(normalizedProcessedFolder);
        var selectionOutputFolderPath = BuildSelectionOutputFolderPath(normalizedProcessedFolder);
        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in Directory.EnumerateFiles(normalizedProcessedFolder, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(filePath);
            if (!fileName.StartsWith("variation_", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var extension = Path.GetExtension(filePath);
            if (!ImageDataHelpers.SupportedExtensions.Contains(extension))
            {
                continue;
            }

            var variationFolderPath = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(variationFolderPath))
            {
                continue;
            }

            var relativeSourcePath = Path.GetRelativePath(normalizedProcessedFolder, variationFolderPath).Replace('\\', '/');
            if (!groups.TryGetValue(relativeSourcePath, out var list))
            {
                list = [];
                groups[relativeSourcePath] = list;
            }

            list.Add(filePath);
        }

        FolderReviewRecord folderRecord;
        lock (_syncRoot)
        {
            folderRecord = GetOrCreateFolderRecord(normalizedProcessedFolder, sourceFolderPath, selectionOutputFolderPath);
        }

        var discoveredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<ProcessReviewItem>(groups.Count);

        foreach (var group in groups.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativeSourcePath = group.Key;
            discoveredPaths.Add(relativeSourcePath);

            var variationFilePaths = group.Value
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, new VariationPathComparer())
                .ToList();

            var originalFilePath = ResolveOriginalFilePath(sourceFolderPath, relativeSourcePath);
            ReviewRecord? reviewRecord;
            lock (_syncRoot)
            {
                folderRecord.Items.TryGetValue(relativeSourcePath, out reviewRecord);
            }

            var isReviewed = reviewRecord is not null
                && reviewRecord.SelectedVariationIndex > 0
                && reviewRecord.SelectedVariationIndex <= variationFilePaths.Count
                && IsSelectionOutputPresent(selectionOutputFolderPath, reviewRecord.SelectedOutputRelativePath);

            items.Add(new ProcessReviewItem
            {
                RelativeSourcePath = relativeSourcePath,
                OriginalFilePath = originalFilePath,
                VariationFolderPath = Path.GetDirectoryName(variationFilePaths[0]) ?? normalizedProcessedFolder,
                VariationFilePaths = variationFilePaths,
                IsReviewed = isReviewed,
                SelectedVariationIndex = isReviewed ? reviewRecord!.SelectedVariationIndex : 0,
                Notes = isReviewed ? reviewRecord!.Notes : string.Empty,
                Transparency = isReviewed && reviewRecord!.Transparency,
                SelectedOutputRelativePath = isReviewed ? reviewRecord!.SelectedOutputRelativePath : string.Empty
            });
        }

        lock (_syncRoot)
        {
            PruneMissingRecords(folderRecord, discoveredPaths);
            SaveIndexToDisk();
        }

        var totalCount = items.Count;
        var reviewedCount = items.Count(item => item.IsReviewed);
        var pendingCount = totalCount - reviewedCount;

        return new ProcessFolderScanResult
        {
            ProcessedFolderPath = normalizedProcessedFolder,
            SourceFolderPath = sourceFolderPath,
            SelectionOutputFolderPath = selectionOutputFolderPath,
            Items = items,
            TotalCount = totalCount,
            ReviewedCount = reviewedCount,
            PendingCount = pendingCount
        };
    }

    private void SaveReviewRecord(
        string processedFolderPath,
        string relativeSourcePath,
        int selectedVariationIndex,
        string notes,
        bool transparency,
        string selectedOutputRelativePath)
    {
        var normalizedProcessedFolder = NormalizeAbsolutePath(processedFolderPath);
        lock (_syncRoot)
        {
            var sourceFolderPath = ResolveSourceFolderPath(normalizedProcessedFolder);
            var outputFolderPath = BuildSelectionOutputFolderPath(normalizedProcessedFolder);
            var folderRecord = GetOrCreateFolderRecord(normalizedProcessedFolder, sourceFolderPath, outputFolderPath);
            folderRecord.Items[relativeSourcePath] = new ReviewRecord
            {
                RelativeSourcePath = relativeSourcePath,
                SelectedVariationIndex = selectedVariationIndex,
                Notes = notes ?? string.Empty,
                Transparency = transparency,
                SelectedOutputRelativePath = selectedOutputRelativePath ?? string.Empty,
                ReviewedAtUtcTicks = DateTime.UtcNow.Ticks
            };

            SaveIndexToDisk();
        }
    }

    private FolderReviewRecord GetOrCreateFolderRecord(string processedFolderPath, string sourceFolderPath, string outputFolderPath)
    {
        if (_folderRecords.TryGetValue(processedFolderPath, out var existing))
        {
            existing.SourceFolderPath = sourceFolderPath;
            existing.SelectionOutputFolderPath = outputFolderPath;
            return existing;
        }

        var created = new FolderReviewRecord
        {
            ProcessedFolderPath = processedFolderPath,
            SourceFolderPath = sourceFolderPath,
            SelectionOutputFolderPath = outputFolderPath
        };
        _folderRecords[processedFolderPath] = created;
        return created;
    }

    private static void PruneMissingRecords(FolderReviewRecord folderRecord, HashSet<string> discoveredPaths)
    {
        var keysToRemove = folderRecord.Items.Keys
            .Where(path => !discoveredPaths.Contains(path))
            .ToList();
        foreach (var key in keysToRemove)
        {
            folderRecord.Items.Remove(key);
        }
    }

    private static string ResolveSourceFolderPath(string processedFolderPath)
    {
        var parentPath = Directory.GetParent(processedFolderPath)?.FullName ?? processedFolderPath;
        var folderName = Path.GetFileName(processedFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (folderName.EndsWith("_processed", StringComparison.OrdinalIgnoreCase))
        {
            var candidateName = folderName[..^"_processed".Length];
            var candidatePath = Path.Combine(parentPath, candidateName);
            return NormalizeAbsolutePath(candidatePath);
        }

        if (folderName.EndsWith("_processed_output", StringComparison.OrdinalIgnoreCase))
        {
            var candidateName = folderName[..^"_processed_output".Length];
            var candidatePath = Path.Combine(parentPath, candidateName);
            return NormalizeAbsolutePath(candidatePath);
        }

        return NormalizeAbsolutePath(processedFolderPath);
    }

    public string BuildSelectionOutputFolderPath(string processedFolderPath)
    {
        var normalizedProcessedPath = NormalizeAbsolutePath(processedFolderPath);
        var parentPath = Directory.GetParent(normalizedProcessedPath)?.FullName ?? normalizedProcessedPath;
        var folderName = Path.GetFileName(normalizedProcessedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        string selectedFolderName;
        if (folderName.EndsWith("_processed", StringComparison.OrdinalIgnoreCase))
        {
            selectedFolderName = $"{folderName[..^"_processed".Length]}_selected";
        }
        else if (folderName.EndsWith("_processed_output", StringComparison.OrdinalIgnoreCase))
        {
            selectedFolderName = $"{folderName[..^"_processed_output".Length]}_selected";
        }
        else
        {
            selectedFolderName = $"{folderName}_selected";
        }

        return Path.Combine(parentPath, selectedFolderName);
    }

    private static string ResolveOriginalFilePath(string sourceFolderPath, string relativeSourcePath)
    {
        var candidatePath = Path.Combine(sourceFolderPath, relativeSourcePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(candidatePath))
        {
            return candidatePath;
        }

        var candidateDirectory = Path.GetDirectoryName(candidatePath);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(candidatePath);
        if (string.IsNullOrWhiteSpace(candidateDirectory) || string.IsNullOrWhiteSpace(fileNameWithoutExtension))
        {
            return candidatePath;
        }

        foreach (var extension in ImageDataHelpers.SupportedExtensions)
        {
            var alternativePath = Path.Combine(candidateDirectory, $"{fileNameWithoutExtension}{extension}");
            if (File.Exists(alternativePath))
            {
                return alternativePath;
            }
        }

        return candidatePath;
    }

    private static bool IsSelectionOutputPresent(string selectionOutputFolderPath, string relativeOutputPath)
    {
        if (string.IsNullOrWhiteSpace(relativeOutputPath))
        {
            return false;
        }

        var outputPath = Path.Combine(selectionOutputFolderPath, relativeOutputPath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(outputPath);
    }

    private static string BuildSelectionOutputPath(string selectionOutputFolderPath, string relativeSourcePath, string selectedExtension)
    {
        var relativeDirectory = Path.GetDirectoryName(relativeSourcePath) ?? string.Empty;
        var baseFileName = Path.GetFileNameWithoutExtension(relativeSourcePath);
        if (string.IsNullOrWhiteSpace(baseFileName))
        {
            baseFileName = "selection";
        }

        var outputRelativePath = Path.Combine(relativeDirectory, $"{baseFileName}{selectedExtension}");
        return Path.Combine(selectionOutputFolderPath, outputRelativePath);
    }

    private static async Task WriteMetadataAsync(string filePath, string notes, bool transparency)
    {
        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
            var keywords = new List<string>
            {
                $"NanoBanana.Transparency={(transparency ? "true" : "false")}"
            };

            if (!string.IsNullOrWhiteSpace(notes))
            {
                keywords.Add($"NanoBanana.Notes={notes}");
            }

            var propertiesToSave = new Dictionary<string, object>
            {
                ["System.Keywords"] = keywords.ToArray(),
                ["System.Comment"] = notes ?? string.Empty
            };

            await storageFile.Properties.SavePropertiesAsync(propertiesToSave);
        }
        catch
        {
            // Metadata writing can fail on some formats; copied output is still valid.
        }
    }

    private void LoadIndexFromDisk()
    {
        lock (_syncRoot)
        {
            _folderRecords.Clear();
            if (!File.Exists(ReviewIndexPath))
            {
                return;
            }

            foreach (var line in File.ReadLines(ReviewIndexPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = line.Split('\t');
                if (parts.Length == 0)
                {
                    continue;
                }

                if (parts[0] == "F" && parts.Length >= 4)
                {
                    var processedFolderPath = Decode(parts[1]);
                    var sourceFolderPath = Decode(parts[2]);
                    var outputFolderPath = Decode(parts[3]);
                    if (string.IsNullOrWhiteSpace(processedFolderPath))
                    {
                        continue;
                    }

                    var normalizedProcessed = NormalizeAbsolutePath(processedFolderPath);
                    _folderRecords[normalizedProcessed] = new FolderReviewRecord
                    {
                        ProcessedFolderPath = normalizedProcessed,
                        SourceFolderPath = string.IsNullOrWhiteSpace(sourceFolderPath) ? ResolveSourceFolderPath(normalizedProcessed) : NormalizeAbsolutePath(sourceFolderPath),
                        SelectionOutputFolderPath = string.IsNullOrWhiteSpace(outputFolderPath) ? BuildSelectionOutputFolderPath(normalizedProcessed) : NormalizeAbsolutePath(outputFolderPath)
                    };
                }
                else if (parts[0] == "R" && parts.Length >= 8)
                {
                    var processedFolderPath = Decode(parts[1]);
                    var relativeSourcePath = Decode(parts[2]);
                    if (string.IsNullOrWhiteSpace(processedFolderPath) || string.IsNullOrWhiteSpace(relativeSourcePath))
                    {
                        continue;
                    }

                    if (!int.TryParse(parts[3], out var selectedVariationIndex))
                    {
                        continue;
                    }

                    var transparency = parts[4] == "1";
                    _ = long.TryParse(parts[5], out var reviewedAtUtcTicks);
                    var notes = Decode(parts[6]);
                    var selectedOutputRelativePath = Decode(parts[7]);

                    var normalizedProcessed = NormalizeAbsolutePath(processedFolderPath);
                    var sourceFolderPath = ResolveSourceFolderPath(normalizedProcessed);
                    var outputFolderPath = BuildSelectionOutputFolderPath(normalizedProcessed);
                    var folderRecord = GetOrCreateFolderRecord(normalizedProcessed, sourceFolderPath, outputFolderPath);
                    folderRecord.Items[relativeSourcePath] = new ReviewRecord
                    {
                        RelativeSourcePath = relativeSourcePath,
                        SelectedVariationIndex = selectedVariationIndex,
                        Notes = notes,
                        Transparency = transparency,
                        SelectedOutputRelativePath = selectedOutputRelativePath,
                        ReviewedAtUtcTicks = reviewedAtUtcTicks
                    };
                }
            }
        }
    }

    private void SaveIndexToDisk()
    {
        Directory.CreateDirectory(SettingsDirectoryPath);

        var lines = new List<string>();
        foreach (var folder in _folderRecords.Values.OrderBy(value => value.ProcessedFolderPath, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add(
                $"F\t{Encode(folder.ProcessedFolderPath)}\t{Encode(folder.SourceFolderPath)}\t{Encode(folder.SelectionOutputFolderPath)}");

            foreach (var review in folder.Items.Values.OrderBy(value => value.RelativeSourcePath, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add(
                    $"R\t{Encode(folder.ProcessedFolderPath)}\t{Encode(review.RelativeSourcePath)}\t{review.SelectedVariationIndex}\t{(review.Transparency ? "1" : "0")}\t{review.ReviewedAtUtcTicks}\t{Encode(review.Notes)}\t{Encode(review.SelectedOutputRelativePath)}");
            }
        }

        File.WriteAllLines(ReviewIndexPath, lines);
    }

    private static int CompareVariationPaths(string? left, string? right)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        var leftIndex = ParseVariationIndex(Path.GetFileNameWithoutExtension(left));
        var rightIndex = ParseVariationIndex(Path.GetFileNameWithoutExtension(right));
        var indexComparison = leftIndex.CompareTo(rightIndex);
        if (indexComparison != 0)
        {
            return indexComparison;
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static int ParseVariationIndex(string? fileNameWithoutExtension)
    {
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
        {
            return int.MaxValue;
        }

        var marker = "variation_";
        var position = fileNameWithoutExtension.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (position < 0)
        {
            return int.MaxValue;
        }

        var value = fileNameWithoutExtension[(position + marker.Length)..];
        return int.TryParse(value, out var index) ? index : int.MaxValue;
    }

    private static string NormalizeAbsolutePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string Encode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
    }

    private static string Decode(string value)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed class VariationPathComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            return CompareVariationPaths(x, y);
        }
    }

    private sealed class FolderReviewRecord
    {
        public required string ProcessedFolderPath { get; init; }

        public required string SourceFolderPath { get; set; }

        public required string SelectionOutputFolderPath { get; set; }

        public Dictionary<string, ReviewRecord> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class ReviewRecord
    {
        public required string RelativeSourcePath { get; init; }

        public required int SelectedVariationIndex { get; init; }

        public required string Notes { get; init; }

        public required bool Transparency { get; init; }

        public required string SelectedOutputRelativePath { get; init; }

        public required long ReviewedAtUtcTicks { get; init; }
    }
}
