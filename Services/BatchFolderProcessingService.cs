using System.Text;

namespace NanoBananaProWinUI.Services;

public sealed class BatchFolderProcessingService
{
    private const char OutputPathsSeparator = '\u001F';
    private static readonly string SettingsDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NanoBananaProWinUI");
    private static readonly string IndexFilePath = Path.Combine(SettingsDirectoryPath, "batch_folder_index.db");

    private readonly object _syncRoot = new();
    private readonly Dictionary<string, FolderRecord> _folderRecords = new(StringComparer.OrdinalIgnoreCase);

    public BatchFolderProcessingService()
    {
        LoadIndexFromDisk();
    }

    public async Task<BatchFolderScanResult> ScanFolderAsync(string sourceFolderPath, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => ScanFolder(sourceFolderPath, cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyList<string>> SaveGeneratedOutputsAsync(
        string sourceFolderPath,
        BatchFileItem sourceFile,
        IReadOnlyList<string> generatedImages,
        OutputImageFormat outputImageFormat,
        CancellationToken cancellationToken = default)
    {
        if (generatedImages.Count == 0)
        {
            return [];
        }

        var normalizedSourcePath = NormalizeAbsolutePath(sourceFolderPath);
        var outputRootPath = BuildOutputFolderPath(normalizedSourcePath);
        Directory.CreateDirectory(outputRootPath);

        var relativeDirectory = Path.GetDirectoryName(sourceFile.RelativePath) ?? string.Empty;
        var sourceFolderName = BuildSafeFileName(Path.GetFileName(sourceFile.Name));
        var outputDirectoryPath = Path.Combine(outputRootPath, relativeDirectory, sourceFolderName);
        Directory.CreateDirectory(outputDirectoryPath);

        var outputRelativePaths = new List<string>(generatedImages.Count);
        for (var index = 0; index < generatedImages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var outputDataUrl = await ImageDataHelpers.ConvertDataUrlForOutputFormatAsync(
                generatedImages[index],
                outputImageFormat,
                cancellationToken);
            var (mimeType, base64Data) = ImageDataHelpers.ParseDataUrl(outputDataUrl);
            var extension = ImageDataHelpers.MimeTypeToExtension(mimeType);
            var outputFileName = $"variation_{index + 1}.{extension}";
            var outputAbsolutePath = Path.Combine(outputDirectoryPath, outputFileName);

            var bytes = Convert.FromBase64String(base64Data);
            await File.WriteAllBytesAsync(outputAbsolutePath, bytes, cancellationToken);

            outputRelativePaths.Add(Path.GetRelativePath(outputRootPath, outputAbsolutePath).Replace('\\', '/'));
        }

        return outputRelativePaths;
    }

    public void MarkFileAsProcessed(
        string sourceFolderPath,
        BatchFileItem sourceFile,
        IReadOnlyList<string> outputRelativePaths)
    {
        var normalizedSourcePath = NormalizeAbsolutePath(sourceFolderPath);
        var outputRootPath = BuildOutputFolderPath(normalizedSourcePath);

        lock (_syncRoot)
        {
            var folderRecord = GetOrCreateFolderRecord(normalizedSourcePath, outputRootPath);
            folderRecord.OutputFolderPath = outputRootPath;
            folderRecord.Files[sourceFile.RelativePath] = new FileRecord
            {
                RelativePath = sourceFile.RelativePath,
                FileSizeBytes = sourceFile.FileSizeBytes,
                LastWriteUtcTicks = sourceFile.LastWriteUtcTicks,
                OutputRelativePaths = outputRelativePaths.ToList(),
                ProcessedAtUtcTicks = DateTime.UtcNow.Ticks
            };

            SaveIndexToDisk();
        }
    }

    public string BuildOutputFolderPath(string sourceFolderPath)
    {
        var normalizedSourcePath = NormalizeAbsolutePath(sourceFolderPath);
        var sourceDirectoryName = Path.GetFileName(normalizedSourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(sourceDirectoryName))
        {
            sourceDirectoryName = "images";
        }

        var parentDirectoryPath = Directory.GetParent(normalizedSourcePath)?.FullName ?? normalizedSourcePath;
        var outputPath = Path.Combine(parentDirectoryPath, $"{sourceDirectoryName}_processed");
        if (string.Equals(outputPath, normalizedSourcePath, StringComparison.OrdinalIgnoreCase))
        {
            outputPath = Path.Combine(parentDirectoryPath, $"{sourceDirectoryName}_processed_output");
        }

        return outputPath;
    }

    private BatchFolderScanResult ScanFolder(string sourceFolderPath, CancellationToken cancellationToken)
    {
        var normalizedSourcePath = NormalizeAbsolutePath(sourceFolderPath);
        if (!Directory.Exists(normalizedSourcePath))
        {
            throw new DirectoryNotFoundException($"Folder does not exist: {normalizedSourcePath}");
        }

        var outputFolderPath = BuildOutputFolderPath(normalizedSourcePath);
        var files = new List<BatchFileItem>();
        var discoveredRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        FolderRecord folderRecord;
        lock (_syncRoot)
        {
            folderRecord = GetOrCreateFolderRecord(normalizedSourcePath, outputFolderPath);
        }

        foreach (var filePath in Directory.EnumerateFiles(normalizedSourcePath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extension = Path.GetExtension(filePath);
            if (!ImageDataHelpers.SupportedExtensions.Contains(extension))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(normalizedSourcePath, filePath).Replace('\\', '/');
            discoveredRelativePaths.Add(relativePath);

            var fileInfo = new FileInfo(filePath);
            folderRecord.Files.TryGetValue(relativePath, out var knownRecord);

            var isProcessed = knownRecord is not null
                && knownRecord.FileSizeBytes == fileInfo.Length
                && knownRecord.LastWriteUtcTicks == fileInfo.LastWriteTimeUtc.Ticks
                && OutputFilesExist(outputFolderPath, knownRecord.OutputRelativePaths);

            files.Add(new BatchFileItem
            {
                Name = relativePath,
                RelativePath = relativePath,
                FullPath = fileInfo.FullName,
                MimeType = ImageDataHelpers.InferMimeType(extension),
                FileSizeBytes = fileInfo.Length,
                LastWriteUtcTicks = fileInfo.LastWriteTimeUtc.Ticks,
                IsProcessed = isProcessed
            });
        }

        files.Sort((left, right) => string.Compare(left.RelativePath, right.RelativePath, StringComparison.OrdinalIgnoreCase));

        lock (_syncRoot)
        {
            if (PruneMissingFiles(folderRecord, discoveredRelativePaths))
            {
                SaveIndexToDisk();
            }
        }

        var totalCount = files.Count;
        var pendingCount = files.Count(file => !file.IsProcessed);
        var processedCount = totalCount - pendingCount;

        return new BatchFolderScanResult
        {
            SourceFolderPath = normalizedSourcePath,
            OutputFolderPath = outputFolderPath,
            Files = files,
            TotalCount = totalCount,
            PendingCount = pendingCount,
            ProcessedCount = processedCount
        };
    }

    private FolderRecord GetOrCreateFolderRecord(string normalizedSourcePath, string outputFolderPath)
    {
        if (_folderRecords.TryGetValue(normalizedSourcePath, out var existingRecord))
        {
            if (!string.Equals(existingRecord.OutputFolderPath, outputFolderPath, StringComparison.OrdinalIgnoreCase))
            {
                existingRecord.OutputFolderPath = outputFolderPath;
            }

            return existingRecord;
        }

        var createdRecord = new FolderRecord
        {
            SourceFolderPath = normalizedSourcePath,
            OutputFolderPath = outputFolderPath
        };
        _folderRecords[normalizedSourcePath] = createdRecord;
        return createdRecord;
    }

    private static bool PruneMissingFiles(FolderRecord folderRecord, HashSet<string> discoveredRelativePaths)
    {
        if (folderRecord.Files.Count == 0)
        {
            return false;
        }

        var removedAny = false;
        var keysToRemove = folderRecord.Files.Keys
            .Where(relativePath => !discoveredRelativePaths.Contains(relativePath))
            .ToList();

        foreach (var key in keysToRemove)
        {
            removedAny = true;
            folderRecord.Files.Remove(key);
        }

        return removedAny;
    }

    private static bool OutputFilesExist(string outputFolderPath, IReadOnlyList<string> outputRelativePaths)
    {
        if (outputRelativePaths.Count == 0)
        {
            return false;
        }

        foreach (var relativePath in outputRelativePaths)
        {
            var absolutePath = Path.Combine(outputFolderPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolutePath))
            {
                return false;
            }
        }

        return true;
    }

    private void LoadIndexFromDisk()
    {
        lock (_syncRoot)
        {
            _folderRecords.Clear();
            if (!File.Exists(IndexFilePath))
            {
                return;
            }

            foreach (var line in File.ReadLines(IndexFilePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var segments = line.Split('\t');
                if (segments.Length == 0)
                {
                    continue;
                }

                if (segments[0] == "F" && segments.Length >= 3)
                {
                    var sourceFolderPath = Decode(segments[1]);
                    var outputFolderPath = Decode(segments[2]);
                    if (string.IsNullOrWhiteSpace(sourceFolderPath) || string.IsNullOrWhiteSpace(outputFolderPath))
                    {
                        continue;
                    }

                    var normalizedSource = NormalizeAbsolutePath(sourceFolderPath);
                    var normalizedOutput = NormalizeAbsolutePath(outputFolderPath);
                    _folderRecords[normalizedSource] = new FolderRecord
                    {
                        SourceFolderPath = normalizedSource,
                        OutputFolderPath = normalizedOutput
                    };
                }
                else if (segments[0] == "R" && segments.Length >= 7)
                {
                    var sourceFolderPath = Decode(segments[1]);
                    var relativePath = Decode(segments[2]);
                    var outputPathsRaw = Decode(segments[5]);

                    if (string.IsNullOrWhiteSpace(sourceFolderPath) || string.IsNullOrWhiteSpace(relativePath))
                    {
                        continue;
                    }

                    if (!long.TryParse(segments[3], out var fileSizeBytes))
                    {
                        continue;
                    }

                    if (!long.TryParse(segments[4], out var lastWriteUtcTicks))
                    {
                        continue;
                    }

                    if (!long.TryParse(segments[6], out var processedAtUtcTicks))
                    {
                        processedAtUtcTicks = 0;
                    }

                    var normalizedSource = NormalizeAbsolutePath(sourceFolderPath);
                    var fallbackOutputPath = BuildOutputFolderPath(normalizedSource);
                    var folderRecord = GetOrCreateFolderRecord(normalizedSource, fallbackOutputPath);

                    var outputRelativePaths = string.IsNullOrWhiteSpace(outputPathsRaw)
                        ? []
                        : outputPathsRaw.Split(OutputPathsSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

                    folderRecord.Files[relativePath] = new FileRecord
                    {
                        RelativePath = relativePath,
                        FileSizeBytes = fileSizeBytes,
                        LastWriteUtcTicks = lastWriteUtcTicks,
                        OutputRelativePaths = outputRelativePaths,
                        ProcessedAtUtcTicks = processedAtUtcTicks
                    };
                }
            }
        }
    }

    private void SaveIndexToDisk()
    {
        Directory.CreateDirectory(SettingsDirectoryPath);

        var lines = new List<string>();
        foreach (var folder in _folderRecords.Values.OrderBy(value => value.SourceFolderPath, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"F\t{Encode(folder.SourceFolderPath)}\t{Encode(folder.OutputFolderPath)}");

            foreach (var file in folder.Files.Values.OrderBy(value => value.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                var outputPaths = string.Join(OutputPathsSeparator, file.OutputRelativePaths);
                lines.Add(
                    $"R\t{Encode(folder.SourceFolderPath)}\t{Encode(file.RelativePath)}\t{file.FileSizeBytes}\t{file.LastWriteUtcTicks}\t{Encode(outputPaths)}\t{file.ProcessedAtUtcTicks}");
            }
        }

        File.WriteAllLines(IndexFilePath, lines);
    }

    private static string NormalizeAbsolutePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string BuildSafeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "image";
        }

        var safeName = value;
        foreach (var invalidFileNameChar in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalidFileNameChar, '_');
        }

        safeName = safeName.Trim();
        return string.IsNullOrWhiteSpace(safeName) ? "image" : safeName;
    }

    private static string Encode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
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

    private sealed class FolderRecord
    {
        public required string SourceFolderPath { get; init; }

        public required string OutputFolderPath { get; set; }

        public Dictionary<string, FileRecord> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class FileRecord
    {
        public required string RelativePath { get; init; }

        public required long FileSizeBytes { get; init; }

        public required long LastWriteUtcTicks { get; init; }

        public required IReadOnlyList<string> OutputRelativePaths { get; init; }

        public required long ProcessedAtUtcTicks { get; init; }
    }
}
