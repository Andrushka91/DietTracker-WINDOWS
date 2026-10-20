using DietTracker.Core.Enums;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace DietTracker.App.Converters;

/// <summary>Maps a meal status to the brush used for the status pill and the card accent.</summary>
public sealed class MealStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        MealStatus.Done => new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 16)),
        MealStatus.Missed => new SolidColorBrush(ColorHelper.FromArgb(255, 164, 38, 44)),
        _ => new SolidColorBrush(ColorHelper.FromArgb(255, 96, 94, 92)),
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>Maps a status to a glyph shown on the card (checkmark, warning, clock).</summary>
public sealed class MealStatusToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        MealStatus.Done => "\uE73E",
        MealStatus.Missed => "\uE7BA",
        _ => "\uE823",
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>Boolean to <see cref="Visibility"/>; the parameter "invert" reverses the mapping.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is true;
        if (parameter is string text && text.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => value is Visibility.Visible;
}

/// <summary>Shows an element only when a count is greater than zero.</summary>
public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>Formats a <see cref="TimeOnly"/> using the current culture and the 12/24 hour preference.</summary>
public sealed class TimeOnlyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not TimeOnly time)
        {
            return string.Empty;
        }

        var use12Hour = parameter is string text && text.Equals("12", StringComparison.OrdinalIgnoreCase);
        return time.ToString(use12Hour ? "h:mm tt" : "HH:mm", System.Globalization.CultureInfo.CurrentCulture);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>Maps a completion percentage (0..100) to the width of the trend bar.</summary>
public sealed class PercentageToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        // A layout property must never receive NaN or an infinity: the native measure fails with
        // COMException (0x80004005) and the application exits.
        var percentage = value is double number && double.IsFinite(number) ? number : 0d;

        var maximum = 100d;
        if (parameter is string text
            && double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            && double.IsFinite(parsed)
            && parsed > 0)
        {
            maximum = parsed;
        }

        var scaled = Math.Clamp(percentage, 0d, 100d) / 100d * maximum;
        return double.IsFinite(scaled) ? scaled : 0d;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}

/// <summary>Returns a color brush from a <c>#AARRGGBB</c> string (used for the custom card colours).</summary>
public sealed class HexColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var text = value as string;
        if (string.IsNullOrWhiteSpace(text))
        {
            return new SolidColorBrush(Colors.Gray);
        }

        var hex = text.TrimStart('#');
        if (hex.Length == 6)
        {
            hex = "FF" + hex;
        }

        if (hex.Length != 8 || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var argb))
        {
            return new SolidColorBrush(Colors.Gray);
        }

        return new SolidColorBrush(ColorHelper.FromArgb(
            (byte)(argb >> 24),
            (byte)(argb >> 16),
            (byte)(argb >> 8),
            (byte)argb));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
