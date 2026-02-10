using Microsoft.UI.Xaml.Data;

namespace NanoBananaProWinUI.Converters;

public sealed class LogTypeToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is LogType type)
        {
            var key = type switch
            {
                LogType.Success => "LogSuccessBrush",
                LogType.Warning => "LogWarningBrush",
                LogType.Error => "LogErrorBrush",
                _ => "LogInfoBrush"
            };

            if (Application.Current.Resources.TryGetValue(key, out var brush))
            {
                return brush;
            }
        }

        return Application.Current.Resources["LogInfoBrush"];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotSupportedException();
    }
}
