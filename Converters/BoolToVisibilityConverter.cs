using Microsoft.UI.Xaml.Data;

namespace NanoBananaProWinUI.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var boolValue = value as bool? ?? false;
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        if (invert)
        {
            boolValue = !boolValue;
        }

        return boolValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility visibility)
        {
            var boolValue = visibility == Visibility.Visible;
            var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
            return invert ? !boolValue : boolValue;
        }

        return false;
    }
}
