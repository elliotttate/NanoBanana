using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using WinRT.Interop;
using Windows.System;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace NanoBananaProWinUI.Views;

public partial class MainPage : Page
{
    private const double WideLayoutBreakpoint = 1320;
    private bool _titleBarConfigured;
    private bool _isDraggingComparisonDivider;
    private AppWindow? _appWindow;

    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
    }

    public MainViewModel ViewModel { get; } = new();

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        ViewModel.Logs.CollectionChanged += Logs_CollectionChanged;
        ConfigureCustomTitleBar();
        if (App.MainAppWindow is not null)
        {
            App.MainAppWindow.SizeChanged += MainAppWindow_SizeChanged;
        }

        SyncModeToggles();
        ApplyResponsiveLayout(ActualWidth);
        UpdateComparisonVisuals();
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        ViewModel.Logs.CollectionChanged -= Logs_CollectionChanged;
        if (App.MainAppWindow is not null)
        {
            App.MainAppWindow.SizeChanged -= MainAppWindow_SizeChanged;
        }
    }

    private void MainPage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout(e.NewSize.Width);
        UpdateComparisonVisuals();
        UpdateTitleBarPadding();
    }

    private void SingleModeToggle_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SwitchMode(AppMode.Single);
        SyncModeToggles();
        UpdateComparisonVisuals();
    }

    private void BatchModeToggle_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SwitchMode(AppMode.Batch);
        SyncModeToggles();
        UpdateComparisonVisuals();
    }

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsSingleMode)
        {
            var imageFile = await PickImageFileAsync();
            if (imageFile is not null)
            {
                await ViewModel.LoadSingleImageAsync(imageFile);
                UpdateComparisonVisuals();
            }
        }
        else
        {
            var zipFile = await PickZipFileAsync();
            if (zipFile is not null)
            {
                await ViewModel.LoadBatchZipAsync(zipFile);
            }
        }
    }

    private async void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.GenerateAsync();
        UpdateComparisonVisuals();
    }

    private async void SaveSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedGeneratedImage is null)
        {
            return;
        }

        var (mimeType, _) = ImageDataHelpers.ParseDataUrl(ViewModel.SelectedGeneratedImage.DataUrl);
        var imageFile = await PickSaveImageFileAsync(ViewModel.OriginalFileName, mimeType);
        if (imageFile is not null)
        {
            await ViewModel.SaveSelectedImageAsync(imageFile);
        }
    }

    private async void DownloadBatchButton_Click(object sender, RoutedEventArgs e)
    {
        var destination = await PickSaveZipFileAsync();
        if (destination is not null)
        {
            await ViewModel.DownloadBatchZipAsync(destination);
        }
    }

    private void ClearLogsButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearLogs();
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var apiKeyTextBox = new TextBox
        {
            Text = ViewModel.ApiKey,
            PlaceholderText = "Gemini API key (optional override)"
        };

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "Connection",
            FontSize = 15
        });
        content.Children.Add(apiKeyTextBox);
        content.Children.Add(new TextBlock
        {
            Text = "If blank, GEMINI_API_KEY (or API_KEY) from your environment is used.",
            TextWrapping = TextWrapping.Wrap
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Settings",
            Content = content,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.ApiKey = apiKeyTextBox.Text?.Trim() ?? string.Empty;
        }
    }

    private void ComparisonSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateComparisonVisuals();
    }

    private void ComparisonSurface_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateComparisonVisuals();
    }

    private void MainPage_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var focusedElement = FocusManager.GetFocusedElement(XamlRoot);
        if (focusedElement is TextBox or RichEditBox or AutoSuggestBox or PasswordBox)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Number1:
            case VirtualKey.NumberPad1:
                SelectGeneratedVariationByIndex(1);
                e.Handled = true;
                break;
            case VirtualKey.Number2:
            case VirtualKey.NumberPad2:
                SelectGeneratedVariationByIndex(2);
                e.Handled = true;
                break;
            case VirtualKey.Number3:
            case VirtualKey.NumberPad3:
                SelectGeneratedVariationByIndex(3);
                e.Handled = true;
                break;
            case VirtualKey.Number4:
            case VirtualKey.NumberPad4:
                SelectGeneratedVariationByIndex(4);
                e.Handled = true;
                break;
        }
    }

    private void ComparisonInteractionLayer_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!ViewModel.HasSelectedGeneratedImage)
        {
            return;
        }

        _isDraggingComparisonDivider = true;
        if (sender is UIElement element)
        {
            element.CapturePointer(e.Pointer);
        }

        UpdateComparisonFromPointer(e);
        e.Handled = true;
    }

    private void ComparisonInteractionLayer_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingComparisonDivider)
        {
            return;
        }

        UpdateComparisonFromPointer(e);
        e.Handled = true;
    }

    private void ComparisonInteractionLayer_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingComparisonDivider)
        {
            return;
        }

        _isDraggingComparisonDivider = false;
        if (sender is UIElement element)
        {
            element.ReleasePointerCapture(e.Pointer);
        }

        UpdateComparisonFromPointer(e);
        e.Handled = true;
    }

    private void ComparisonInteractionLayer_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (!ViewModel.HasSelectedGeneratedImage || ComparisonSurface is null || ComparisonSurface.ActualWidth <= 0)
        {
            return;
        }

        var position = e.GetPosition(ComparisonSurface);
        var clampedX = Math.Clamp(position.X, 0, ComparisonSurface.ActualWidth);
        ViewModel.ComparisonValue = (clampedX / ComparisonSurface.ActualWidth) * 100d;
        e.Handled = true;
    }

    private async Task<StorageFile?> PickImageFileAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };

        foreach (var extension in ImageDataHelpers.SupportedExtensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        InitializePicker(picker);
        return await picker.PickSingleFileAsync();
    }

    private async Task<StorageFile?> PickZipFileAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };

        picker.FileTypeFilter.Add(".zip");
        InitializePicker(picker);
        return await picker.PickSingleFileAsync();
    }

    private async Task<StorageFile?> PickSaveImageFileAsync(string originalFileName, string mimeType)
    {
        var extension = "." + ImageDataHelpers.MimeTypeToExtension(mimeType);
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "generated_image";
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SuggestedFileName = $"{baseName}_variation"
        };

        picker.FileTypeChoices.Add("Image", [extension]);
        InitializePicker(picker);
        return await picker.PickSaveFileAsync();
    }

    private async Task<StorageFile?> PickSaveZipFileAsync()
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = "generated_textures"
        };

        picker.FileTypeChoices.Add("Zip archive", [".zip"]);
        InitializePicker(picker);
        return await picker.PickSaveFileAsync();
    }

    private static void InitializePicker(object picker)
    {
        var window = App.MainAppWindow ?? throw new InvalidOperationException("Main application window is not available.");
        var windowHandle = WindowNative.GetWindowHandle(window);
        InitializeWithWindow.Initialize(picker, windowHandle);
    }

    private void ApplyResponsiveLayout(double pageWidth)
    {
        var wideLayout = pageWidth >= WideLayoutBreakpoint;
        var showResultsPanel = ViewModel.ShowResultsPanel;

        if (wideLayout && showResultsPanel)
        {
            MainContentColumn0.Width = new GridLength(390);
            MainContentColumn1.Width = new GridLength(1, GridUnitType.Star);
            MainContentRow0.Height = new GridLength(1, GridUnitType.Star);
            MainContentRow1.Height = new GridLength(0);

            Grid.SetRow(ControlsPanel, 0);
            Grid.SetColumn(ControlsPanel, 0);
            Grid.SetRow(ResultsPanel, 0);
            Grid.SetColumn(ResultsPanel, 1);
        }
        else
        {
            MainContentColumn0.Width = new GridLength(1, GridUnitType.Star);
            MainContentColumn1.Width = new GridLength(0);
            MainContentRow0.Height = GridLength.Auto;
            MainContentRow1.Height = showResultsPanel ? GridLength.Auto : new GridLength(0);

            Grid.SetRow(ControlsPanel, 0);
            Grid.SetColumn(ControlsPanel, 0);
            Grid.SetRow(ResultsPanel, 1);
            Grid.SetColumn(ResultsPanel, 0);
        }
    }

    private void UpdateComparisonVisuals()
    {
        if (GeneratedImageClip is null || ComparisonSurface is null || ComparisonDivider is null)
        {
            return;
        }

        if (!ViewModel.HasSelectedGeneratedImage || ComparisonSurface.ActualWidth <= 0 || ComparisonSurface.ActualHeight <= 0)
        {
            GeneratedImageClip.Rect = new Windows.Foundation.Rect(0, 0, 0, 0);
            ComparisonDivider.Visibility = Visibility.Collapsed;
            return;
        }

        var clampedValue = Math.Clamp(ViewModel.ComparisonValue, 0, 100);
        var clipWidth = ComparisonSurface.ActualWidth * (clampedValue / 100d);
        GeneratedImageClip.Rect = new Windows.Foundation.Rect(0, 0, clipWidth, ComparisonSurface.ActualHeight);

        ComparisonDivider.Visibility = Visibility.Visible;
        ComparisonDivider.Height = ComparisonSurface.ActualHeight;
        Canvas.SetLeft(ComparisonDivider, Math.Max(0, clipWidth - (ComparisonDivider.Width / 2)));
        Canvas.SetTop(ComparisonDivider, 0);
    }

    private void UpdateComparisonFromPointer(PointerRoutedEventArgs e)
    {
        if (ComparisonSurface is null || ComparisonSurface.ActualWidth <= 0)
        {
            return;
        }

        var point = e.GetCurrentPoint(ComparisonSurface).Position;
        var clampedX = Math.Clamp(point.X, 0, ComparisonSurface.ActualWidth);
        ViewModel.ComparisonValue = (clampedX / ComparisonSurface.ActualWidth) * 100d;
    }

    private void SelectGeneratedVariationByIndex(int index)
    {
        var variation = ViewModel.GeneratedImages.FirstOrDefault(image => image.Index == index);
        if (variation is null)
        {
            return;
        }

        ViewModel.SelectedGeneratedImage = variation;
        if (GeneratedImagesGridView is not null)
        {
            GeneratedImagesGridView.SelectedItem = variation;
            GeneratedImagesGridView.ScrollIntoView(variation);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Mode))
        {
            SyncModeToggles();
        }

        if (e.PropertyName is nameof(MainViewModel.ShowResultsPanel))
        {
            ApplyResponsiveLayout(ActualWidth);
        }

        if (e.PropertyName is nameof(MainViewModel.SelectedGeneratedImage) or nameof(MainViewModel.ComparisonValue))
        {
            UpdateComparisonVisuals();
        }
    }

    private void Logs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            if (LogsScrollViewer is not null)
            {
                LogsScrollViewer.ChangeView(null, LogsScrollViewer.ScrollableHeight, null, disableAnimation: true);
            }
        });
    }

    private void SyncModeToggles()
    {
        if (SingleModeToggle is null || BatchModeToggle is null)
        {
            return;
        }

        SingleModeToggle.IsChecked = ViewModel.IsSingleMode;
        BatchModeToggle.IsChecked = ViewModel.IsBatchMode;
    }

    private void MainAppWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
    {
        UpdateTitleBarPadding();
    }

    private void ConfigureCustomTitleBar()
    {
        if (_titleBarConfigured)
        {
            UpdateTitleBarPadding();
            return;
        }

        var window = App.MainAppWindow;
        if (window is null || AppTitleBar is null || !AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(AppTitleBar);

        var hWnd = WindowNative.GetWindowHandle(window);
        var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow is not null)
        {
            var titleBar = _appWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = ColorHelper.FromArgb(0xFF, 0xE8, 0xE8, 0xE8);
            titleBar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(0xFF, 0x98, 0x98, 0x98);
            titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(0xFF, 0x2A, 0x2A, 0x2A);
            titleBar.ButtonHoverForegroundColor = Colors.White;
            titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(0xFF, 0x3A, 0x3A, 0x3A);
            titleBar.ButtonPressedForegroundColor = Colors.White;
        }

        _titleBarConfigured = true;
        UpdateTitleBarPadding();
    }

    private void UpdateTitleBarPadding()
    {
        if (!_titleBarConfigured || _appWindow is null || TitleBarLeftPaddingColumn is null || TitleBarRightPaddingColumn is null)
        {
            return;
        }

        TitleBarLeftPaddingColumn.Width = new GridLength(_appWindow.TitleBar.LeftInset);
        TitleBarRightPaddingColumn.Width = new GridLength(_appWindow.TitleBar.RightInset);
    }
}
