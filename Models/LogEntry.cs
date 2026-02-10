namespace NanoBananaProWinUI.Models;

public enum LogType
{
    Info,
    Success,
    Warning,
    Error
}

public sealed class LogEntry
{
    public required string Timestamp { get; init; }

    public required string Message { get; init; }

    public required LogType Type { get; init; }
}
