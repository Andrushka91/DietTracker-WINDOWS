using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace DietTracker.App.Converters;

/// <summary>
/// Subtracts a fixed gutter (<c>ConverterParameter</c>, invariant-culture double) from the bound
/// width. Used to cap the dashboard content stack inside the ScrollViewer padding, so the two card
/// columns stay within the visible window no matter how wide the ScrollViewer measures its content.
/// </summary>
public sealed class WidthMinusConverter : IValueConverter
{
    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double width
            && double.TryParse(parameter as string, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gutter))
        {
            return Math.Max(0d, width - gutter);
        }

        return value;
    }

    /// <inheritdoc />
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

/// <summary>
/// Shows an element only when the bound text has content. The confirmation lines of the settings
/// cards use it so an empty message reserves no height: the last visible row of every card then
/// ends on the same bottom padding, no matter whether it is a button or a text.
/// </summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is string text && !string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;

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


