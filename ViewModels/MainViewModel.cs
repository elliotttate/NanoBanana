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
        "Warming up the Nano Banana Pro...",
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
        nameof(HasError),
        nameof(HasOriginalImage),
        nameof(HasGeneratedImages),
        nameof(HasSelectedGeneratedImage),
        nameof(SelectedGeneratedPreviewImage),
        nameof(HasBatchFiles),
        nameof(HasBatchResults),
        nameof(HasBatchProgress),
        nameof(HasLogs),
        nameof(ShowOriginalFileInfo),
        nameof(ShowBatchInfo),
        nameof(CanEditPrompt),
        nameof(CanGenerate),
        nameof(CanSaveSelectedImage),
        nameof(CanDownloadBatchZip),
        nameof(ShowSingleLoadingState),
        nameof(ShowSingleEmptyState),
        nameof(ShowSingleOriginalState),
        nameof(ShowSingleGeneratedState),
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
    };

    private readonly GeminiImageService _geminiImageService;
    private readonly ZipProcessingService _zipProcessingService;
    private readonly DispatcherQueueTimer _loadingMessageTimer;
    private readonly List<BatchGenerationResult> _batchResults = [];
    private int _loadingMessageIndex;

    private string? _originalBase64Data;
    private string? _originalMimeType;

    public MainViewModel()
    {
        Title = "NanoBanana Image Editor";
        _geminiImageService = new GeminiImageService();
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
    private bool _isLogsVisible = false;

    [ObservableProperty]
    private double _resultScalePercent = 100;

    public bool IsSingleMode => Mode == AppMode.Single;

    public bool IsBatchMode => Mode == AppMode.Batch;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasOriginalImage => OriginalPreviewImage is not null;

    public bool HasGeneratedImages => GeneratedImages.Count > 0;

    public bool HasSelectedGeneratedImage => SelectedGeneratedImage is not null;

    public BitmapImage? SelectedGeneratedPreviewImage => SelectedGeneratedImage?.PreviewImage;

    public bool HasBatchFiles => BatchFiles.Count > 0;

    public bool HasBatchResults => _batchResults.Count > 0;

    public bool HasBatchProgress => BatchProgressTotal > 0;

    public bool HasLogs => Logs.Count > 0;

    public bool ShowOriginalFileInfo => IsSingleMode && HasOriginalImage;

    public bool ShowBatchInfo => IsBatchMode && HasBatchFiles;

    public bool CanEditPrompt => IsSingleMode ? HasOriginalImage : HasBatchFiles;

    public bool CanGenerate => !IsLoading && !string.IsNullOrWhiteSpace(Prompt) && (IsSingleMode ? HasOriginalImage : HasBatchFiles);

    public bool CanSaveSelectedImage => !IsLoading && HasSelectedGeneratedImage;

    public bool CanDownloadBatchZip => !IsZipping && HasBatchResults;

    public bool ShowSingleLoadingState => IsSingleMode && IsLoading;

    public bool ShowSingleEmptyState => IsSingleMode && !IsLoading && !HasOriginalImage;

    public bool ShowSingleOriginalState => IsSingleMode && !IsLoading && HasOriginalImage && !HasGeneratedImages;

    public bool ShowSingleGeneratedState => IsSingleMode && !IsLoading && HasGeneratedImages;

    public bool ShowResultsPanel => IsBatchMode || IsLoading || HasOriginalImage || HasGeneratedImages;

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

    public string BatchSummaryText => $"Ready to generate 4 variations for each of these {BatchFiles.Count} images.";

    public string BatchCompleteText => $"Generated 4 variations for {_batchResults.Count} images.";

    public string PromptPlaceholder => IsSingleMode
        ? "e.g., \"Add dramatic cinematic highlights and richer texture detail\""
        : "e.g., \"Convert all textures to seamless PBR materials, high detail\"";

    public string GenerateButtonText => IsBatchMode
        ? IsLoading ? "Processing Batch..." : "Generate Batch"
        : IsLoading ? "Generating..." : "Generate Edits";

    public string UploadButtonText => IsSingleMode
        ? HasOriginalImage ? "Change Image" : "Select Image"
        : HasBatchFiles ? "Change ZIP" : "Select ZIP File";

    public string UploadGlyph => IsSingleMode ? "\uE898" : "\uE7B8";

    public string OriginalFileLabel => string.IsNullOrWhiteSpace(OriginalFileName) ? string.Empty : $"File: {OriginalFileName}";

    public string BatchFoundText => HasBatchFiles ? $"Found {BatchFiles.Count} images" : string.Empty;

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

    public async Task LoadBatchZipAsync(StorageFile file)
    {
        if (!string.Equals(file.FileType, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Please upload a .zip file for batch processing.";
            AddLog("Invalid file type uploaded. Expected .zip", LogType.Warning);
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;
        BatchFiles.Clear();
        ClearBatchResults();
        BatchProgressCurrent = 0;
        BatchProgressTotal = 0;

        try
        {
            AddLog($"Reading zip file: {file.Name}...", LogType.Info);
            var images = await _zipProcessingService.ExtractImagesFromZipAsync(file);
            if (images.Count == 0)
            {
                ErrorMessage = "No valid images (jpg, png, webp) found in the zip file.";
                AddLog("Zip file contained no valid images.", LogType.Error);
                return;
            }

            foreach (var image in images)
            {
                BatchFiles.Add(image);
            }

            Prompt = "Convert these textures to seamless PBR materials, high quality, 8k resolution";
            AddLog($"Found {images.Count} images in zip file.", LogType.Success);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to read zip file: {ex.Message}";
            AddLog($"Error reading zip file: {ex.Message}", LogType.Error);
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
        else
        {
            await GenerateBatchAsync();
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
        if (!HasBatchFiles || string.IsNullOrWhiteSpace(Prompt))
        {
            ErrorMessage = "Please upload a zip with images and enter a prompt.";
            AddLog("Attempted batch generation without zip or prompt.", LogType.Warning);
            return;
        }

        IsLoading = true;
        ErrorMessage = string.Empty;
        ClearBatchResults();
        BatchProgressCurrent = 0;
        BatchProgressTotal = BatchFiles.Count;
        AddLog($"Starting batch processing for {BatchFiles.Count} files.", LogType.Info);

        try
        {
            var key = ResolveApiKey();

            for (var i = 0; i < BatchFiles.Count; i++)
            {
                var file = BatchFiles[i];
                try
                {
                    AddLog($"[{i + 1}/{BatchFiles.Count}] Processing: {file.Name}...", LogType.Info);
                    var images = await _geminiImageService.GenerateEditedImagesAsync(
                        key,
                        file.Base64Data,
                        file.MimeType,
                        Prompt.Trim(),
                        ImageSize);

                    _batchResults.Add(new BatchGenerationResult
                    {
                        OriginalName = file.Name,
                        GeneratedImages = images
                    });

                    AddLog($"[{i + 1}/{BatchFiles.Count}] Success: {file.Name}", LogType.Success);
                }
                catch (Exception ex)
                {
                    AddLog($"[{i + 1}/{BatchFiles.Count}] Failed: {file.Name} - {ex.Message}", LogType.Error);

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
                $"Batch processing complete. Generated results for {_batchResults.Count} out of {BatchFiles.Count} files.",
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
}
