using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace NanoBananaProWinUI.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    private static readonly string SettingsDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NanoBananaProWinUI");

    private static readonly string ApiKeyFilePath = Path.Combine(SettingsDirectoryPath, "gemini_api_key.txt");

    private static readonly string[] LoadingMessages =
    {
        "Warming up NanoBanana...",
        "Painting with pixels...",
        "Reticulating splines...",
        "Generating creative variations...",
        "Consulting with the digital muse...",
        "Almost there..."
    };

    private static readonly string[] ComputedPropertyNames =
    {
        nameof(IsSingleMode),
        nameof(IsBatchMode),
        nameof(IsProcessMode),
        nameof(IsSingleOrProcessMode),
        nameof(HasError),
        nameof(HasOriginalImage),
        nameof(HasGeneratedImages),
        nameof(HasSelectedGeneratedImage),
        nameof(SelectedGeneratedPreviewImage),
        nameof(HasBatchFiles),
        nameof(HasBatchFolder),
        nameof(HasPendingBatchFiles),
        nameof(HasBatchResults),
        nameof(HasBatchProgress),
        nameof(HasProcessFolder),
        nameof(HasProcessItems),
        nameof(HasCurrentProcessItem),
        nameof(HasPendingProcessItems),
        nameof(IsRedoQueueActive),
        nameof(HasLogs),
        nameof(ShowOriginalFileInfo),
        nameof(ShowBatchInfo),
        nameof(ShowProcessInfo),
        nameof(CanEditPrompt),
        nameof(CanGenerate),
        nameof(CanSaveSelectedImage),
        nameof(CanSaveProcessSelection),
        nameof(CanRedoProcessItem),
        nameof(CanDownloadBatchZip),
        nameof(ShowSingleLoadingState),
        nameof(ShowSingleEmptyState),
        nameof(ShowSingleOriginalState),
        nameof(ShowSingleGeneratedState),
        nameof(ShowComparisonWorkspace),
        nameof(ShowProcessLoadingState),
        nameof(ShowProcessEmptyState),
        nameof(ShowProcessReviewState),
        nameof(ShowProcessCompleteState),
        nameof(ShowResultsPanel),
        nameof(ShowBatchLoadingState),
        nameof(ShowBatchInitializingState),
        nameof(ShowBatchEmptyState),
        nameof(ShowBatchReadyState),
        nameof(ShowBatchCompleteState),
        nameof(BatchProgressPercent),
        nameof(BatchProgressText),
        nameof(BatchSummaryText),
        nameof(BatchCompleteText),
        nameof(SingleResultViewportHeight),
        nameof(ResultScaleLabel),
        nameof(PromptPlaceholder),
        nameof(GenerateButtonText),
        nameof(UploadButtonText),
        nameof(UploadGlyph),
        nameof(OriginalFileLabel),
        nameof(BatchFoundText),
        nameof(BatchSourceFolderLabel),
        nameof(BatchOutputFolderLabel),
        nameof(ProcessFoundText),
        nameof(ProcessProcessedFolderLabel),
        nameof(ProcessSourceFolderLabel),
        nameof(ProcessSelectionFolderLabel),
        nameof(ProcessCurrentItemLabel),
        nameof(ProcessProgressText),
    };

    private readonly GeminiImageService _geminiImageService;
    private readonly BatchFolderProcessingService _batchFolderProcessingService;
    private readonly ProcessReviewService _processReviewService;
    private readonly ZipProcessingService _zipProcessingService;
    private readonly DispatcherQueueTimer _loadingMessageTimer;
    private readonly List<BatchGenerationResult> _batchResults = [];
    private readonly List<ProcessReviewItem> _processItems = [];
    private readonly Queue<ProcessRedoRequest> _processRedoQueue = new();
    private readonly object _processRedoQueueSync = new();
    private int _loadingMessageIndex;
    private int _batchProcessedThisRunCount;
    private int _currentProcessItemIndex = -1;
    private bool _isProcessRedoWorkerRunning;
    private CancellationTokenSource _processRedoCancellation = new();

    private string? _originalBase64Data;
    private string? _originalMimeType;

    public MainViewModel()
    {
        Title = "NanoBanana Image Editor";
        _geminiImageService = new GeminiImageService();
        _batchFolderProcessingService = new BatchFolderProcessingService();
        _processReviewService = new ProcessReviewService();
        _zipProcessingService = new ZipProcessingService();

        var dispatcherQueue = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("MainViewModel must be created on the UI thread.");

        _loadingMessageTimer = dispatcherQueue.CreateTimer();
        _loadingMessageTimer.Interval = TimeSpan.FromSeconds(2);
        _loadingMessageTimer.Tick += (_, _) => RotateLoadingMessage();

        ApiKey = LoadApiKeyFromSettings();

        GeneratedImages.CollectionChanged += (_, _) => RefreshComputedProperties();
        BatchFiles.CollectionChanged += (_, _) => RefreshComputedProperties();
        Logs.CollectionChanged += OnLogsChanged;
    }

    public ObservableCollection<GeneratedImageVariation> GeneratedImages { get; } = [];

    public ObservableCollection<BatchFileItem> BatchFiles { get; } = [];

    public ObservableCollection<LogEntry> Logs { get; } = [];

    public IReadOnlyList<BatchGenerationResult> BatchResults => _batchResults;

    [ObservableProperty]
    private AppMode _mode = AppMode.Single;

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private string _imageSize = "1K";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isZipping;

    [ObservableProperty]
    private string _loadingMessage = LoadingMessages[0];

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private BitmapImage? _originalPreviewImage;

    [ObservableProperty]
    private string _originalFileName = string.Empty;

    [ObservableProperty]
    private GeneratedImageVariation? _selectedGeneratedImage;

    [ObservableProperty]
    private double _comparisonValue = 50;

    [ObservableProperty]
    private int _batchProgressCurrent;

    [ObservableProperty]
    private int _batchProgressTotal;

    [ObservableProperty]
    private string _batchSourceFolderPath = string.Empty;

    [ObservableProperty]
    private string _batchOutputFolderPath = string.Empty;

    [ObservableProperty]
    private int _batchPendingCount;

    [ObservableProperty]
    private int _batchProcessedCount;

    [ObservableProperty]
    private string _processProcessedFolderPath = string.Empty;

    [ObservableProperty]
    private string _processSourceFolderPath = string.Empty;

    [ObservableProperty]
    private string _processSelectionFolderPath = string.Empty;

    [ObservableProperty]
    private string _processCurrentRelativePath = string.Empty;

    [ObservableProperty]
    private int _processTotalCount;

    [ObservableProperty]
    private int _processReviewedCount;

    [ObservableProperty]
    private int _processPendingCount;

    [ObservableProperty]
    private string _processNotes = string.Empty;

    [ObservableProperty]
    private bool _processTransparency;

    [ObservableProperty]
    private bool _isLogsVisible = false;

    [ObservableProperty]
    private double _resultScalePercent = 100;

    public bool IsSingleMode => Mode == AppMode.Single;

    public bool IsBatchMode => Mode == AppMode.Batch;

    public bool IsProcessMode => Mode == AppMode.Process;

    public bool IsSingleOrProcessMode => IsSingleMode || IsProcessMode;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasOriginalImage => OriginalPreviewImage is not null;

    public bool HasGeneratedImages => GeneratedImages.Count > 0;

    public bool HasSelectedGeneratedImage => SelectedGeneratedImage is not null;

    public BitmapImage? SelectedGeneratedPreviewImage => SelectedGeneratedImage?.PreviewImage;

    public bool HasBatchFiles => BatchFiles.Count > 0;

    public bool HasBatchFolder => !string.IsNullOrWhiteSpace(BatchSourceFolderPath);

    public bool HasPendingBatchFiles => BatchPendingCount > 0;

    public bool HasBatchResults => _batchResults.Count > 0;

    public bool HasBatchProgress => BatchProgressTotal > 0;

    public bool HasProcessFolder => !string.IsNullOrWhiteSpace(ProcessProcessedFolderPath);

    public bool HasProcessItems => _processItems.Count > 0;

    public bool HasCurrentProcessItem => _currentProcessItemIndex >= 0 && _currentProcessItemIndex < _processItems.Count;

    public bool HasPendingProcessItems => ProcessPendingCount > 0;

    public bool IsRedoQueueActive
    {
        get
        {
            lock (_processRedoQueueSync)
            {
                return _isProcessRedoWorkerRunning || _processRedoQueue.Count > 0;
            }
        }
    }

    public bool HasLogs => Logs.Count > 0;

    public bool ShowOriginalFileInfo => IsSingleMode && HasOriginalImage;

    public bool ShowBatchInfo => IsBatchMode && HasBatchFolder;

    public bool ShowProcessInfo => IsProcessMode && HasProcessFolder;

    public bool CanEditPrompt => IsSingleMode
        ? HasOriginalImage
        : IsBatchMode
            ? HasBatchFolder
            : HasCurrentProcessItem;

    public bool CanGenerate => !IsLoading
        && !string.IsNullOrWhiteSpace(Prompt)
        && (IsSingleMode ? HasOriginalImage : IsBatchMode ? HasPendingBatchFiles : HasCurrentProcessItem);

    public bool CanSaveSelectedImage => !IsLoading && HasSelectedGeneratedImage && (IsSingleMode || IsProcessMode);

    public bool CanSaveProcessSelection => IsProcessMode && HasCurrentProcessItem && HasSelectedGeneratedImage && !IsLoading;

    public bool CanRedoProcessItem => IsProcessMode && HasCurrentProcessItem && !IsLoading && !string.IsNullOrWhiteSpace(Prompt);

    public bool CanDownloadBatchZip => !IsZipping && HasBatchResults;

    public bool ShowSingleLoadingState => IsSingleMode && IsLoading;

    public bool ShowSingleEmptyState => IsSingleMode && !IsLoading && !HasOriginalImage;

    public bool ShowSingleOriginalState => IsSingleMode && !IsLoading && HasOriginalImage && !HasGeneratedImages;

    public bool ShowSingleGeneratedState => IsSingleMode && !IsLoading && HasGeneratedImages;

    public bool ShowComparisonWorkspace => ShowSingleGeneratedState || ShowProcessReviewState;

    public bool ShowProcessLoadingState => IsProcessMode && IsLoading && !HasCurrentProcessItem;

    public bool ShowProcessEmptyState => IsProcessMode && !IsLoading && !HasProcessFolder;

    public bool ShowProcessReviewState => IsProcessMode && !IsLoading && HasCurrentProcessItem;

    public bool ShowProcessCompleteState => IsProcessMode && !IsLoading && HasProcessFolder && !HasCurrentProcessItem;

    public bool ShowResultsPanel => IsBatchMode || IsProcessMode || IsLoading || HasOriginalImage || HasGeneratedImages;

    public bool ShowBatchLoadingState => IsBatchMode && IsLoading && HasBatchProgress;

    public bool ShowBatchInitializingState => IsBatchMode && IsLoading && !HasBatchProgress;

    public bool ShowBatchEmptyState => IsBatchMode && !IsLoading && !HasBatchFiles;

    public bool ShowBatchReadyState => IsBatchMode && !IsLoading && HasBatchFiles && !HasBatchResults && !HasBatchProgress;

    public bool ShowBatchCompleteState => IsBatchMode && !IsLoading && HasBatchResults;

    public double SingleResultViewportHeight => Math.Clamp(560d * (ResultScalePercent / 100d), 320d, 980d);

    public string ResultScaleLabel => $"{Math.Round(ResultScalePercent)}%";

    public double BatchProgressPercent => BatchProgressTotal <= 0
        ? 0
        : Math.Clamp((double)BatchProgressCurrent / BatchProgressTotal * 100, 0, 100);

    public string BatchProgressText => BatchProgressTotal <= 0
        ? "Preparing..."
        : $"Image {Math.Min(BatchProgressCurrent, BatchProgressTotal)} of {BatchProgressTotal}";

    public string BatchSummaryText => !HasBatchFiles
        ? string.Empty
        : $"Total images: {BatchFiles.Count}  |  Pending: {BatchPendingCount}  |  Already processed: {BatchProcessedCount}";

    public string BatchCompleteText => _batchProcessedThisRunCount <= 0
        ? "No new files were processed."
        : $"Processed {_batchProcessedThisRunCount} new files. Output folder: {BatchOutputFolderPath}";

    public string PromptPlaceholder => IsSingleMode
        ? "e.g., \"Add dramatic cinematic highlights and richer texture detail\""
        : IsBatchMode
            ? "e.g., \"Convert all textures to seamless PBR materials, high detail\""
            : "e.g., \"Regenerate with cleaner edges and stronger contrast\"";

    public string GenerateButtonText => IsBatchMode
        ? IsLoading ? "Processing Batch..." : "Generate Batch"
        : IsProcessMode
            ? "Redo & Next"
            : IsLoading ? "Generating..." : "Generate Edits";

    public string UploadButtonText => IsSingleMode
        ? HasOriginalImage ? "Change Image" : "Select Image"
        : IsBatchMode
            ? HasBatchFolder ? "Change Folder" : "Select Folder"
            : HasProcessFolder ? "Change Processed Folder" : "Select Processed Folder";

    public string UploadGlyph => IsSingleMode ? "\uE898" : "\uE8B7";

    public string OriginalFileLabel => string.IsNullOrWhiteSpace(OriginalFileName) ? string.Empty : $"File: {OriginalFileName}";

    public string BatchFoundText => HasBatchFiles
        ? $"Found {BatchFiles.Count} images ({BatchPendingCount} pending, {BatchProcessedCount} processed)"
        : HasBatchFolder ? "No supported images found in this folder." : string.Empty;

    public string BatchSourceFolderLabel => HasBatchFolder ? $"Folder: {BatchSourceFolderPath}" : string.Empty;

    public string BatchOutputFolderLabel => HasBatchFolder ? $"Output: {BatchOutputFolderPath}" : string.Empty;

    public string ProcessFoundText => HasProcessItems
        ? $"Found {_processItems.Count} images ({ProcessPendingCount} pending, {ProcessReviewedCount} reviewed)"
        : HasProcessFolder ? "No processed image sets found in this folder." : string.Empty;

    public string ProcessProcessedFolderLabel => HasProcessFolder ? $"Processed Folder: {ProcessProcessedFolderPath}" : string.Empty;

    public string ProcessSourceFolderLabel => HasProcessFolder ? $"Original Folder: {ProcessSourceFolderPath}" : string.Empty;

    public string ProcessSelectionFolderLabel => HasProcessFolder ? $"Selections Folder: {ProcessSelectionFolderPath}" : string.Empty;

    public string ProcessCurrentItemLabel => HasCurrentProcessItem
        ? $"Current: {ProcessCurrentRelativePath} ({_currentProcessItemIndex + 1}/{Math.Max(1, ProcessTotalCount)})"
        : string.Empty;

    public string ProcessProgressText => HasProcessFolder
        ? $"Reviewed {ProcessReviewedCount}/{ProcessTotalCount}  |  Pending {ProcessPendingCount}"
        : string.Empty;

    public void SwitchMode(AppMode newMode)
    {
        if (Mode == newMode)
        {
            return;
        }

        Mode = newMode;
        ResetApplicationState();
    }

    public async Task LoadSingleImageAsync(StorageFile file)
    {
        var bytes = await ReadFileBytesAsync(file);
        _originalBase64Data = Convert.ToBase64String(bytes);
        _originalMimeType = ImageDataHelpers.InferMimeType(file.FileType);
        OriginalPreviewImage = await ImageDataHelpers.CreateBitmapImageAsync(bytes);
        OriginalFileName = file.Name;

        GeneratedImages.Clear();
        SelectedGeneratedImage = null;
        ComparisonValue = 50;
        ErrorMessage = string.Empty;
        Prompt = BuildPromptFromFileName(file.Name);

        AddLog($"Image loaded: {file.Name}", LogType.Info);
        RefreshComputedProperties();
    }

    public async Task LoadBatchFolderAsync(StorageFolder folder)
    {
        if (folder is null || string.IsNullOrWhiteSpace(folder.Path))
        {
            ErrorMessage = "Please select a valid folder for batch processing.";
            AddLog("Folder selection was invalid.", LogType.Warning);
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;
        BatchFiles.Clear();
        ClearBatchResults();
        BatchProgressCurrent = 0;
        BatchProgressTotal = 0;
        BatchSourceFolderPath = string.Empty;
        BatchOutputFolderPath = string.Empty;
        BatchPendingCount = 0;
        BatchProcessedCount = 0;
        _batchProcessedThisRunCount = 0;

        try
        {
            AddLog($"Scanning folder: {folder.Path}", LogType.Info);
            var scanResult = await _batchFolderProcessingService.ScanFolderAsync(folder.Path);

            BatchSourceFolderPath = scanResult.SourceFolderPath;
            BatchOutputFolderPath = scanResult.OutputFolderPath;
            BatchPendingCount = scanResult.PendingCount;
            BatchProcessedCount = scanResult.ProcessedCount;

            foreach (var image in scanResult.Files)
            {
                BatchFiles.Add(image);
            }

            if (scanResult.TotalCount == 0)
            {
                ErrorMessage = "No valid images (.jpg, .jpeg, .png, .webp) found in the selected folder.";
                AddLog("Selected folder contained no supported images.", LogType.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(Prompt))
            {
                Prompt = "Convert these textures to seamless PBR materials, high quality, 8k resolution";
            }

            AddLog(
                $"Found {scanResult.TotalCount} images. Pending: {scanResult.PendingCount}, already processed: {scanResult.ProcessedCount}.",
                LogType.Success);
            AddLog($"Processed outputs will be saved to: {scanResult.OutputFolderPath}", LogType.Info);

            if (scanResult.PendingCount == 0)
            {
                AddLog("No new or changed files found. Add images and run Generate Batch to process only the new files.", LogType.Info);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to read folder: {ex.Message}";
            AddLog($"Error reading folder: {ex.Message}", LogType.Error);
        }
        finally
        {
            IsLoading = false;
            RefreshComputedProperties();
        }
    }

    public async Task LoadProcessFolderAsync(StorageFolder folder)
    {
        if (folder is null || string.IsNullOrWhiteSpace(folder.Path))
        {
            ErrorMessage = "Please select a valid processed folder.";
            AddLog("Processed folder selection was invalid.", LogType.Warning);
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;

        _processRedoCancellation.Cancel();
        _processRedoCancellation.Dispose();
        _processRedoCancellation = new CancellationTokenSource();
        lock (_processRedoQueueSync)
        {
            _processRedoQueue.Clear();
            _isProcessRedoWorkerRunning = false;
        }

        _processItems.Clear();
        _currentProcessItemIndex = -1;
        ProcessProcessedFolderPath = string.Empty;
        ProcessSourceFolderPath = string.Empty;
        ProcessSelectionFolderPath = string.Empty;
        ProcessCurrentRelativePath = string.Empty;
        ProcessTotalCount = 0;
        ProcessReviewedCount = 0;
        ProcessPendingCount = 0;
        ProcessNotes = string.Empty;
        ProcessTransparency = false;
        GeneratedImages.Clear();
        SelectedGeneratedImage = null;
        OriginalPreviewImage = null;
        OriginalFileName = string.Empty;
        ComparisonValue = 50;

        try
        {
            AddLog($"Scanning processed folder: {folder.Path}", LogType.Info);
            var scanResult = await _processReviewService.ScanProcessedFolderAsync(folder.Path);

            ProcessProcessedFolderPath = scanResult.ProcessedFolderPath;
            ProcessSourceFolderPath = scanResult.SourceFolderPath;
            ProcessSelectionFolderPath = scanResult.SelectionOutputFolderPath;
            ProcessTotalCount = scanResult.TotalCount;
            ProcessReviewedCount = scanResult.ReviewedCount;
            ProcessPendingCount = scanResult.PendingCount;
            _processItems.AddRange(scanResult.Items);

            if (_processItems.Count == 0)
            {
                ErrorMessage = "No processed image sets (variation_1..4) were found in this folder.";
                AddLog("No valid processed sets found.", LogType.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(Prompt))
            {
                Prompt = "Improve details, clean edges, and keep the same composition";
            }

            AddLog(
                $"Loaded {_processItems.Count} review items. Pending: {ProcessPendingCount}, reviewed: {ProcessReviewedCount}.",
                LogType.Success);
            AddLog($"Selections will be saved to: {ProcessSelectionFolderPath}", LogType.Info);

            var firstPendingIndex = _processItems.FindIndex(item => !item.IsReviewed);
            var targetIndex = firstPendingIndex >= 0 ? firstPendingIndex : 0;
            await LoadProcessItemAsync(targetIndex);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load processed folder: {ex.Message}";
            AddLog($"Error loading processed folder: {ex.Message}", LogType.Error);
        }
        finally
        {
            IsLoading = false;
            RefreshComputedProperties();
        }
    }

    public async Task GenerateAsync()
    {
        if (IsSingleMode)
        {
            await GenerateSingleAsync();
        }
        else if (IsBatchMode)
        {
            await GenerateBatchAsync();
        }
        else
        {
            await RedoProcessSelectionAndAdvanceAsync();
        }
    }

    public async Task SaveSelectedImageAsync(StorageFile destinationFile)
    {
        if (SelectedGeneratedImage is null)
        {
            return;
        }

        try
        {
            var bytes = ImageDataHelpers.DataUrlToBytes(SelectedGeneratedImage.DataUrl, out _);
            await FileIO.WriteBytesAsync(destinationFile, bytes);
            AddLog($"Image saved: {destinationFile.Name}", LogType.Info);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save image: {ex.Message}";
            AddLog($"Failed to save image: {ex.Message}", LogType.Error);
        }
    }

    public async Task SaveProcessSelectionAndAdvanceAsync()
    {
        if (!CanSaveProcessSelection || !HasCurrentProcessItem)
        {
            return;
        }

        try
        {
            var currentItem = _processItems[_currentProcessItemIndex];
            var selectedVariationIndex = SelectedGeneratedImage?.Index ?? 0;
            if (selectedVariationIndex <= 0)
            {
                AddLog("No variation selected for process save.", LogType.Warning);
                return;
            }

            var outputPath = await _processReviewService.SaveSelectionAsync(
                ProcessProcessedFolderPath,
                ProcessSelectionFolderPath,
                currentItem,
                selectedVariationIndex,
                ProcessNotes.Trim(),
                ProcessTransparency);

            if (!currentItem.IsReviewed)
            {
                currentItem.IsReviewed = true;
                ProcessReviewedCount = Math.Min(ProcessTotalCount, ProcessReviewedCount + 1);
                ProcessPendingCount = Math.Max(0, ProcessPendingCount - 1);
            }

            currentItem.SelectedVariationIndex = selectedVariationIndex;
            currentItem.Notes = ProcessNotes.Trim();
            currentItem.Transparency = ProcessTransparency;
            currentItem.SelectedOutputRelativePath = Path.GetRelativePath(ProcessSelectionFolderPath, outputPath).Replace('\\', '/');

            AddLog($"Saved selection for {currentItem.RelativeSourcePath} to {outputPath}", LogType.Success);
            await MoveToNextPendingProcessItemAsync(skipCurrentItem: true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to save process selection: {ex.Message}";
            AddLog($"Failed to save process selection: {ex.Message}", LogType.Error);
        }
    }

    public async Task RedoProcessSelectionAndAdvanceAsync()
    {
        if (!CanRedoProcessItem || !HasCurrentProcessItem)
        {
            return;
        }

        var currentItem = _processItems[_currentProcessItemIndex];
        var request = new ProcessRedoRequest(
            currentItem,
            Prompt.Trim(),
            ImageSize);

        lock (_processRedoQueueSync)
        {
            _processRedoQueue.Enqueue(request);
        }

        currentItem.IsReviewed = false;
        currentItem.SelectedVariationIndex = 0;
        currentItem.Notes = string.Empty;
        currentItem.Transparency = false;
        currentItem.SelectedOutputRelativePath = string.Empty;
        ProcessPendingCount = _processItems.Count(item => !item.IsReviewed);
        ProcessReviewedCount = _processItems.Count - ProcessPendingCount;
        _processReviewService.ClearReviewRecord(ProcessProcessedFolderPath, currentItem.RelativeSourcePath);

        AddLog($"Queued redo for {currentItem.RelativeSourcePath}.", LogType.Info);
        EnsureProcessRedoWorker();
        await MoveToNextPendingProcessItemAsync(skipCurrentItem: true);
        RefreshComputedProperties();
    }

    public async Task DownloadBatchZipAsync(StorageFile destinationFile)
    {
        if (!HasBatchResults)
        {
            return;
        }

        IsZipping = true;
        AddLog("Creating ZIP archive...", LogType.Info);

        try
        {
            var zipBytes = await _zipProcessingService.CreateBatchZipAsync(_batchResults);
            await FileIO.WriteBytesAsync(destinationFile, zipBytes);
            AddLog("ZIP archive downloaded successfully.", LogType.Success);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create zip file: {ex.Message}";
            AddLog($"Failed to create ZIP archive: {ex.Message}", LogType.Error);
        }
        finally
        {
            IsZipping = false;
        }
    }

    public void ClearLogs()
    {
        Logs.Clear();
    }

    private async Task GenerateSingleAsync()
    {
        if (!HasOriginalImage || string.IsNullOrWhiteSpace(Prompt) || string.IsNullOrWhiteSpace(_originalBase64Data) || string.IsNullOrWhiteSpace(_originalMimeType))
        {
            ErrorMessage = "Please upload an image and enter a prompt.";
            AddLog("Attempted generation without image or prompt.", LogType.Warning);
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;
        GeneratedImages.Clear();
        SelectedGeneratedImage = null;
        ComparisonValue = 50;
        AddLog($"Starting single image generation for: {OriginalFileName}", LogType.Info);

        try
        {
            var key = ResolveApiKey();
            var generatedDataUrls = await _geminiImageService.GenerateEditedImagesAsync(
                key,
                _originalBase64Data,
                _originalMimeType,
                Prompt.Trim(),
                ImageSize);

            var index = 1;
            foreach (var dataUrl in generatedDataUrls)
            {
                var bytes = ImageDataHelpers.DataUrlToBytes(dataUrl, out _);
                var preview = await ImageDataHelpers.CreateBitmapImageAsync(bytes);

                GeneratedImages.Add(new GeneratedImageVariation
                {
                    Index = index,
                    DataUrl = dataUrl,
                    PreviewImage = preview
                });

                index++;
            }

            if (GeneratedImages.Count > 0)
            {
                SelectedGeneratedImage = GeneratedImages[0];
            }

            AddLog("Generation successful.", LogType.Success);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Generation failed: {ex.Message}";
            AddLog($"Generation failed: {ex.Message}", LogType.Error);
        }
        finally
        {
            IsLoading = false;
            RefreshComputedProperties();
        }
    }

    private async Task GenerateBatchAsync()
    {
        if (!HasBatchFiles || string.IsNullOrWhiteSpace(Prompt) || string.IsNullOrWhiteSpace(BatchSourceFolderPath))
        {
            ErrorMessage = "Please select a folder with images and enter a prompt.";
            AddLog("Attempted batch generation without folder or prompt.", LogType.Warning);
            return;
        }

        var pendingFiles = BatchFiles.Where(file => !file.IsProcessed).ToList();
        if (pendingFiles.Count == 0)
        {
            ErrorMessage = string.Empty;
            AddLog("No new files to process. Batch is already up to date.", LogType.Info);
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;
        ClearBatchResults();
        BatchProgressCurrent = 0;
        BatchProgressTotal = pendingFiles.Count;
        _batchProcessedThisRunCount = 0;

        AddLog($"Starting batch processing for {pendingFiles.Count} pending files.", LogType.Info);
        AddLog($"Output folder: {BatchOutputFolderPath}", LogType.Info);

        try
        {
            var key = ResolveApiKey();

            for (var i = 0; i < pendingFiles.Count; i++)
            {
                var file = pendingFiles[i];
                try
                {
                    AddLog($"[{i + 1}/{pendingFiles.Count}] Processing: {file.RelativePath}...", LogType.Info);
                    var sourceBytes = await File.ReadAllBytesAsync(file.FullPath);
                    var sourceBase64Data = Convert.ToBase64String(sourceBytes);

                    var images = await _geminiImageService.GenerateEditedImagesAsync(
                        key,
                        sourceBase64Data,
                        file.MimeType,
                        Prompt.Trim(),
                        ImageSize);

                    var outputRelativePaths = await _batchFolderProcessingService.SaveGeneratedOutputsAsync(
                        BatchSourceFolderPath,
                        file,
                        images);

                    _batchFolderProcessingService.MarkFileAsProcessed(BatchSourceFolderPath, file, outputRelativePaths);
                    file.IsProcessed = true;
                    BatchPendingCount = Math.Max(0, BatchPendingCount - 1);
                    BatchProcessedCount++;
                    _batchProcessedThisRunCount++;

                    _batchResults.Add(new BatchGenerationResult
                    {
                        OriginalName = file.Name,
                        OriginalRelativePath = file.RelativePath,
                        GeneratedImages = images
                    });

                    AddLog($"[{i + 1}/{pendingFiles.Count}] Success: {file.RelativePath}", LogType.Success);
                }
                catch (Exception ex)
                {
                    AddLog($"[{i + 1}/{pendingFiles.Count}] Failed: {file.RelativePath} - {ex.Message}", LogType.Error);

                    if (IsRateLimitError(ex.Message))
                    {
                        AddLog("Rate limit/Quota detected. Pausing for 10 seconds to cool down...", LogType.Warning);
                        await Task.Delay(TimeSpan.FromSeconds(10));
                    }
                }

                BatchProgressCurrent = i + 1;
                RefreshComputedProperties();
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            AddLog(
                $"Batch processing complete. Processed {_batchProcessedThisRunCount} out of {pendingFiles.Count} pending files.",
                LogType.Success);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Batch generation interrupted: {ex.Message}";
            AddLog($"Batch interrupted: {ex.Message}", LogType.Error);
        }
        finally
        {
            IsLoading = false;
            RefreshComputedProperties();
        }
    }

    private void EnsureProcessRedoWorker()
    {
        lock (_processRedoQueueSync)
        {
            if (_isProcessRedoWorkerRunning)
            {
                return;
            }

            _isProcessRedoWorkerRunning = true;
        }

        _ = ProcessRedoQueueAsync(_processRedoCancellation.Token);
        RefreshComputedProperties();
    }

    private async Task ProcessRedoQueueAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                ProcessRedoRequest request;
                lock (_processRedoQueueSync)
                {
                    if (_processRedoQueue.Count == 0)
                    {
                        break;
                    }

                    request = _processRedoQueue.Dequeue();
                }

                try
                {
                    AddLog($"[Redo] Starting: {request.Item.RelativeSourcePath}", LogType.Info);

                    if (!File.Exists(request.Item.OriginalFilePath))
                    {
                        throw new FileNotFoundException("Original file for redo was not found.", request.Item.OriginalFilePath);
                    }

                    var sourceBytes = await File.ReadAllBytesAsync(request.Item.OriginalFilePath, cancellationToken);
                    var sourceBase64 = Convert.ToBase64String(sourceBytes);
                    var mimeType = ImageDataHelpers.InferMimeType(request.Item.OriginalFilePath);
                    var key = ResolveApiKey();

                    var generatedImages = await _geminiImageService.GenerateEditedImagesAsync(
                        key,
                        sourceBase64,
                        mimeType,
                        request.Prompt,
                        request.ImageSize,
                        cancellationToken);

                    var variationFilePaths = await _processReviewService.OverwriteVariationsAsync(request.Item, generatedImages, cancellationToken);
                    request.Item.VariationFilePaths = variationFilePaths;
                    request.Item.IsReviewed = false;
                    request.Item.SelectedVariationIndex = 0;
                    request.Item.Notes = string.Empty;
                    request.Item.Transparency = false;
                    request.Item.SelectedOutputRelativePath = string.Empty;
                    _processReviewService.ClearReviewRecord(ProcessProcessedFolderPath, request.Item.RelativeSourcePath);

                    ProcessPendingCount = _processItems.Count(item => !item.IsReviewed);
                    ProcessReviewedCount = _processItems.Count - ProcessPendingCount;

                    AddLog($"[Redo] Completed: {request.Item.RelativeSourcePath}", LogType.Success);

                    if (HasCurrentProcessItem && _processItems[_currentProcessItemIndex].RelativeSourcePath == request.Item.RelativeSourcePath)
                    {
                        await LoadProcessItemAsync(_currentProcessItemIndex);
                    }
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException)
                    {
                        break;
                    }

                    AddLog($"[Redo] Failed: {request.Item.RelativeSourcePath} - {ex.Message}", LogType.Error);
                }

                RefreshComputedProperties();
            }
        }
        finally
        {
            lock (_processRedoQueueSync)
            {
                _isProcessRedoWorkerRunning = false;
            }

            RefreshComputedProperties();
        }
    }

    private async Task MoveToNextPendingProcessItemAsync(bool skipCurrentItem)
    {
        if (_processItems.Count == 0)
        {
            _currentProcessItemIndex = -1;
            ProcessCurrentRelativePath = string.Empty;
            GeneratedImages.Clear();
            SelectedGeneratedImage = null;
            OriginalPreviewImage = null;
            OriginalFileName = string.Empty;
            ProcessNotes = string.Empty;
            ProcessTransparency = false;
            RefreshComputedProperties();
            return;
        }

        var startIndex = _currentProcessItemIndex;
        var scanStart = skipCurrentItem ? Math.Max(0, startIndex + 1) : Math.Max(0, startIndex);
        var nextIndex = FindPendingProcessItemIndex(scanStart, skipCurrentItem ? startIndex : -1);

        if (nextIndex < 0)
        {
            nextIndex = FindPendingProcessItemIndex(0, skipCurrentItem ? startIndex : -1);
        }

        if (nextIndex < 0
            && skipCurrentItem
            && startIndex >= 0
            && startIndex < _processItems.Count
            && !_processItems[startIndex].IsReviewed)
        {
            nextIndex = startIndex;
        }

        if (nextIndex < 0)
        {
            _currentProcessItemIndex = -1;
            ProcessCurrentRelativePath = string.Empty;
            GeneratedImages.Clear();
            SelectedGeneratedImage = null;
            OriginalPreviewImage = null;
            OriginalFileName = string.Empty;
            ProcessNotes = string.Empty;
            ProcessTransparency = false;
            AddLog("All process items are reviewed. Add new outputs or use redo to continue.", LogType.Success);
            RefreshComputedProperties();
            return;
        }

        await LoadProcessItemAsync(nextIndex);
    }

    private int FindPendingProcessItemIndex(int startIndex, int skipIndex)
    {
        for (var index = startIndex; index < _processItems.Count; index++)
        {
            if (index == skipIndex)
            {
                continue;
            }

            if (!_processItems[index].IsReviewed)
            {
                return index;
            }
        }

        return -1;
    }

    private async Task LoadProcessItemAsync(int index)
    {
        if (index < 0 || index >= _processItems.Count)
        {
            return;
        }

        _currentProcessItemIndex = index;
        var item = _processItems[index];
        ProcessCurrentRelativePath = item.RelativeSourcePath;
        ProcessNotes = item.Notes;
        ProcessTransparency = item.Transparency;
        ComparisonValue = 50;
        ErrorMessage = string.Empty;

        GeneratedImages.Clear();
        SelectedGeneratedImage = null;

        if (File.Exists(item.OriginalFilePath))
        {
            var originalBytes = await File.ReadAllBytesAsync(item.OriginalFilePath);
            OriginalPreviewImage = await ImageDataHelpers.CreateBitmapImageAsync(originalBytes);
            OriginalFileName = item.RelativeSourcePath;
        }
        else
        {
            OriginalPreviewImage = null;
            OriginalFileName = item.RelativeSourcePath;
            AddLog($"Original file missing for {item.RelativeSourcePath}. Comparison will show generated only.", LogType.Warning);
        }

        var variationIndex = 1;
        foreach (var variationPath in item.VariationFilePaths)
        {
            if (!File.Exists(variationPath))
            {
                continue;
            }

            var bytes = await File.ReadAllBytesAsync(variationPath);
            var mimeType = ImageDataHelpers.InferMimeType(variationPath);
            var dataUrl = ImageDataHelpers.BuildDataUrl(mimeType, Convert.ToBase64String(bytes));
            var preview = await ImageDataHelpers.CreateBitmapImageAsync(bytes);

            GeneratedImages.Add(new GeneratedImageVariation
            {
                Index = variationIndex,
                DataUrl = dataUrl,
                PreviewImage = preview
            });

            variationIndex++;
        }

        if (GeneratedImages.Count > 0)
        {
            var selected = item.SelectedVariationIndex > 0
                ? GeneratedImages.FirstOrDefault(image => image.Index == item.SelectedVariationIndex)
                : null;
            SelectedGeneratedImage = selected ?? GeneratedImages[0];
        }

        AddLog($"Reviewing {item.RelativeSourcePath}", LogType.Info);
        RefreshComputedProperties();
    }

    private static async Task<byte[]> ReadFileBytesAsync(StorageFile file)
    {
        using var readStream = await file.OpenReadAsync();
        using var netStream = readStream.AsStreamForRead();
        using var memoryStream = new MemoryStream();
        await netStream.CopyToAsync(memoryStream);
        return memoryStream.ToArray();
    }

    private static string BuildPromptFromFileName(string fileName)
    {
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var imageName = nameWithoutExtension.Replace('_', ' ').Replace('-', ' ');
        var words = imageName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]);
        var formattedName = string.Join(" ", words);

        if (string.IsNullOrWhiteSpace(formattedName))
        {
            formattedName = "Texture";
        }

        return $"Creatively reimagine / upscale this {formattedName} texture in great detail to high definition";
    }

    private string ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            return ApiKey.Trim();
        }

        var environmentKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? Environment.GetEnvironmentVariable("API_KEY");

        if (!string.IsNullOrWhiteSpace(environmentKey))
        {
            return environmentKey.Trim();
        }

        throw new InvalidOperationException("Set GEMINI_API_KEY (or API_KEY), or enter an API key in the app.");
    }

    private static bool IsRateLimitError(string message)
    {
        return message.Contains("429", StringComparison.OrdinalIgnoreCase)
               || message.Contains("quota", StringComparison.OrdinalIgnoreCase)
               || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
    }

    private static string LoadApiKeyFromSettings()
    {
        try
        {
            if (!File.Exists(ApiKeyFilePath))
            {
                return string.Empty;
            }

            return File.ReadAllText(ApiKeyFilePath).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void SaveApiKeyToSettings(string value)
    {
        try
        {
            var trimmed = value.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                if (File.Exists(ApiKeyFilePath))
                {
                    File.Delete(ApiKeyFilePath);
                }

                return;
            }

            Directory.CreateDirectory(SettingsDirectoryPath);
            File.WriteAllText(ApiKeyFilePath, trimmed);
        }
        catch
        {
            // Ignore persistence failures; generation can still use env vars or in-memory key.
        }
    }

    private void AddLog(string message, LogType type)
    {
        Logs.Add(new LogEntry
        {
            Timestamp = DateTime.Now.ToString("HH:mm:ss"),
            Message = message,
            Type = type
        });
    }

    private void ClearBatchResults()
    {
        _batchResults.Clear();
        RefreshComputedProperties();
    }

    private void ResetApplicationState()
    {
        _loadingMessageTimer.Stop();
        _loadingMessageIndex = 0;
        LoadingMessage = LoadingMessages[_loadingMessageIndex];
        IsLoading = false;
        IsZipping = false;
        ErrorMessage = string.Empty;
        Prompt = string.Empty;

        _originalBase64Data = null;
        _originalMimeType = null;
        OriginalPreviewImage = null;
        OriginalFileName = string.Empty;
        GeneratedImages.Clear();
        SelectedGeneratedImage = null;
        ComparisonValue = 50;

        BatchFiles.Clear();
        ClearBatchResults();
        BatchProgressCurrent = 0;
        BatchProgressTotal = 0;
        BatchSourceFolderPath = string.Empty;
        BatchOutputFolderPath = string.Empty;
        BatchPendingCount = 0;
        BatchProcessedCount = 0;
        _batchProcessedThisRunCount = 0;

        _processItems.Clear();
        _currentProcessItemIndex = -1;
        _processRedoCancellation.Cancel();
        _processRedoCancellation.Dispose();
        _processRedoCancellation = new CancellationTokenSource();
        lock (_processRedoQueueSync)
        {
            _processRedoQueue.Clear();
            _isProcessRedoWorkerRunning = false;
        }

        ProcessProcessedFolderPath = string.Empty;
        ProcessSourceFolderPath = string.Empty;
        ProcessSelectionFolderPath = string.Empty;
        ProcessCurrentRelativePath = string.Empty;
        ProcessTotalCount = 0;
        ProcessReviewedCount = 0;
        ProcessPendingCount = 0;
        ProcessNotes = string.Empty;
        ProcessTransparency = false;

        Logs.Clear();
        AddLog("Reset application state.", LogType.Info);
        RefreshComputedProperties();
    }

    private void RotateLoadingMessage()
    {
        _loadingMessageIndex = (_loadingMessageIndex + 1) % LoadingMessages.Length;
        LoadingMessage = LoadingMessages[_loadingMessageIndex];
    }

    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasLogs));
    }

    private void RefreshComputedProperties()
    {
        foreach (var propertyName in ComputedPropertyNames)
        {
            OnPropertyChanged(propertyName);
        }
    }

    partial void OnModeChanged(AppMode value)
    {
        RefreshComputedProperties();
    }

    partial void OnPromptChanged(string value)
    {
        RefreshComputedProperties();
    }

    partial void OnApiKeyChanged(string value)
    {
        SaveApiKeyToSettings(value);
        RefreshComputedProperties();
    }

    partial void OnImageSizeChanged(string value)
    {
        RefreshComputedProperties();
    }

    partial void OnIsLoadingChanged(bool value)
    {
        if (value)
        {
            _loadingMessageIndex = 0;
            LoadingMessage = LoadingMessages[_loadingMessageIndex];
            _loadingMessageTimer.Start();
        }
        else
        {
            _loadingMessageTimer.Stop();
            _loadingMessageIndex = 0;
            LoadingMessage = LoadingMessages[_loadingMessageIndex];
        }

        RefreshComputedProperties();
    }

    partial void OnIsZippingChanged(bool value)
    {
        RefreshComputedProperties();
    }

    partial void OnLoadingMessageChanged(string value)
    {
        RefreshComputedProperties();
    }

    partial void OnErrorMessageChanged(string value)
    {
        RefreshComputedProperties();
    }

    partial void OnOriginalPreviewImageChanged(BitmapImage? value)
    {
        RefreshComputedProperties();
    }

    partial void OnOriginalFileNameChanged(string value)
    {
        RefreshComputedProperties();
    }

    partial void OnSelectedGeneratedImageChanged(GeneratedImageVariation? value)
    {
        if (value is not null)
        {
            ComparisonValue = 50;

            if (IsProcessMode && HasCurrentProcessItem)
            {
                _processItems[_currentProcessItemIndex].SelectedVariationIndex = value.Index;
            }
        }

        RefreshComputedProperties();
    }

    partial void OnComparisonValueChanged(double value)
    {
        RefreshComputedProperties();
    }

    partial void OnBatchProgressCurrentChanged(int value)
    {
        RefreshComputedProperties();
    }

    partial void OnBatchProgressTotalChanged(int value)
    {
        RefreshComputedProperties();
    }

    partial void OnBatchSourceFolderPathChanged(string value)
    {
        RefreshComputedProperties();
    }

    partial void OnBatchOutputFolderPathChanged(string value)
    {
        RefreshComputedProperties();
    }

    partial void OnBatchPendingCountChanged(int value)
    {
        RefreshComputedProperties();
    }

    partial void OnBatchProcessedCountChanged(int value)
    {
        RefreshComputedProperties();
    }

    partial void OnProcessProcessedFolderPathChanged(string value)
    {
        RefreshComputedProperties();
    }

    partial void OnProcessSourceFolderPathChanged(string value)
    {
        RefreshComputedProperties();
    }

    partial void OnProcessSelectionFolderPathChanged(string value)
    {
        RefreshComputedProperties();
    }

    partial void OnProcessCurrentRelativePathChanged(string value)
    {
        RefreshComputedProperties();
    }

    partial void OnProcessTotalCountChanged(int value)
    {
        RefreshComputedProperties();
    }

    partial void OnProcessReviewedCountChanged(int value)
    {
        RefreshComputedProperties();
    }

    partial void OnProcessPendingCountChanged(int value)
    {
        RefreshComputedProperties();
    }

    partial void OnProcessNotesChanged(string value)
    {
        if (HasCurrentProcessItem)
        {
            _processItems[_currentProcessItemIndex].Notes = value;
        }

        RefreshComputedProperties();
    }

    partial void OnProcessTransparencyChanged(bool value)
    {
        if (HasCurrentProcessItem)
        {
            _processItems[_currentProcessItemIndex].Transparency = value;
        }

        RefreshComputedProperties();
    }

    partial void OnResultScalePercentChanged(double value)
    {
        var clamped = Math.Clamp(value, 70d, 150d);
        if (Math.Abs(clamped - value) > 0.001)
        {
            ResultScalePercent = clamped;
            return;
        }

        RefreshComputedProperties();
    }

    private sealed record ProcessRedoRequest(ProcessReviewItem Item, string Prompt, string ImageSize);
}
