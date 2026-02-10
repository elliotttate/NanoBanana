using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NanoBananaProWinUI.Models;

public sealed class BatchFileItem : INotifyPropertyChanged
{
    public required string Name { get; init; }

    public required string RelativePath { get; init; }

    public required string FullPath { get; init; }

    public required string MimeType { get; init; }

    public required long FileSizeBytes { get; init; }

    public required long LastWriteUtcTicks { get; init; }

    private bool _isProcessed;

    public bool IsProcessed
    {
        get => _isProcessed;
        set
        {
            if (_isProcessed == value)
            {
                return;
            }

            _isProcessed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusLabel));
        }
    }

    public string StatusLabel => IsProcessed ? "Processed" : "Pending";

    public string ShortType => MimeType.Split('/').LastOrDefault() ?? "img";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
