using DietTracker.Core.Interfaces;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace DietTracker.App.Services;

/// <summary>
/// Localization backed by the compiled English resource file (MRT Core), so every user visible
/// string lives in the single en-US Resources.resw file and none is hardcoded in XAML or C#.
/// </summary>
public sealed class ResourceLocalizationService : ILocalizationService
{
    private readonly ResourceLoader _loader = new();

    /// <inheritdoc />
    public string Get(string resourceKey)
    {
        if (string.IsNullOrEmpty(resourceKey))
        {
            return string.Empty;
        }

        try
        {
            var value = _loader.GetString(resourceKey);

            // A missing key is surfaced as the key itself, which makes localization gaps obvious in the
            // UI and in the automated resource test.
            return string.IsNullOrEmpty(value) ? resourceKey : value;
        }
        catch (Exception)
        {
            return resourceKey;
        }
    }

    /// <inheritdoc />
    public string Format(string resourceKey, params object?[] args)
    {
        var format = Get(resourceKey);
        try
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, format, args);
        }
        catch (FormatException)
        {
            return format;
        }
    }
}

/// <summary>UI agnostic dialogs implemented with <see cref="ContentDialog"/> and the main window root.</summary>
public sealed class DialogService : IDialogService
{
    private readonly ILocalizationService _localization;

    public DialogService(ILocalizationService localization) => _localization = localization;

    /// <inheritdoc />
    public async Task<bool> ConfirmAsync(string titleResourceKey, string messageResourceKey, string? detail = null)
    {
        var dialog = BuildDialog(titleResourceKey, messageResourceKey, detail);
        dialog.PrimaryButtonText = _localization.Get("Common_Yes");
        dialog.CloseButtonText = _localization.Get("Common_No");
        dialog.DefaultButton = ContentDialogButton.Close;

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <inheritdoc />
    public async Task<bool> OfferAsync(string titleResourceKey, string messageResourceKey, string primaryResourceKey, string? detail = null)
    {
        var dialog = BuildDialog(titleResourceKey, messageResourceKey, detail);
        dialog.PrimaryButtonText = _localization.Get(primaryResourceKey);
        dialog.CloseButtonText = _localization.Get("Common_Ok");
        dialog.DefaultButton = ContentDialogButton.Close;

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <inheritdoc />
    public async Task ShowMessageAsync(string titleResourceKey, string messageResourceKey, string? detail = null)
    {
        var dialog = BuildDialog(titleResourceKey, messageResourceKey, detail);
        dialog.CloseButtonText = _localization.Get("Common_Close");
        await dialog.ShowAsync();
    }

    /// <inheritdoc />
    public async Task<int> ChooseAsync(string titleResourceKey, string messageResourceKey, IReadOnlyList<string> optionResourceKeys)
    {
        var dialog = BuildDialog(titleResourceKey, messageResourceKey, null);
        dialog.PrimaryButtonText = _localization.Get("Common_Ok");
        dialog.CloseButtonText = _localization.Get("Common_Cancel");
        dialog.DefaultButton = ContentDialogButton.Primary;

        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            ItemsSource = optionResourceKeys.Select(_localization.Get).ToArray(),
            MinWidth = 320,
        };

        // The first option is preselected, so "OK" always confirms something sensible and a plain
        // "Cancel" (or a dialog dismissed without a choice) still reports -1.
        if (list.Items.Count > 0)
        {
            list.SelectedIndex = 0;
        }

        dialog.Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = _localization.Get(messageResourceKey), TextWrapping = TextWrapping.Wrap },
                list,
            },
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? list.SelectedIndex : -1;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The whole choice lives in this one popup: two plain options, a single date field and a range
    /// field - editing a field selects its option, so no follow-up dialog is ever opened. The
    /// content is one column with a min width that keeps the two range fields side by side.
    /// </remarks>
    public async Task<CalendarExportChoice?> ChooseCalendarExportAsync(DateOnly initialDate)
    {
        var dialog = BuildDialog("CalendarExport_ChooseTitle", "CalendarExport_ChooseMessage", null);
        dialog.PrimaryButtonText = _localization.Get("Common_Ok");
        dialog.CloseButtonText = _localization.Get("Common_Cancel");
        dialog.DefaultButton = ContentDialogButton.Primary;

        var initial = initialDate.ToDateTime(TimeOnly.MinValue);

        var currentRadio = new RadioButton
        {
            Content = _localization.Get("CalendarExport_CurrentMonth"),
            IsChecked = true,
            GroupName = "CalendarScope",
        };
        var yearRadio = new RadioButton
        {
            Content = _localization.Get("CalendarExport_WholeYear"),
            GroupName = "CalendarScope",
        };
        var singleRadio = new RadioButton
        {
            Content = _localization.Get("CalendarExport_SingleDate"),
            GroupName = "CalendarScope",
        };
        var rangeRadio = new RadioButton
        {
            Content = _localization.Get("CalendarExport_DateRange"),
            GroupName = "CalendarScope",
        };

        var singlePicker = new CalendarDatePicker { Date = initial, MinWidth = 240 };
        var fromPicker = new CalendarDatePicker { Date = initial, MinWidth = 150 };
        var toPicker = new CalendarDatePicker { Date = initial, MinWidth = 150 };

        // Touching a date field selects its option: the field the user just edited is the one that
        // gets exported, without an extra click on the radio button above it.
        singlePicker.DateChanged += (_, _) => singleRadio.IsChecked = true;
        fromPicker.DateChanged += (_, _) => rangeRadio.IsChecked = true;
        toPicker.DateChanged += (_, _) => rangeRadio.IsChecked = true;

        var singleField = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
            Margin = new Thickness(28, 0, 0, 0),
            Children = { singlePicker },
        };

        var rangeFields = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(28, 0, 0, 0),
            Children =
            {
                fromPicker,
                new TextBlock { Text = "-", VerticalAlignment = VerticalAlignment.Center },
                toPicker,
            },
        };

        dialog.Content = new StackPanel
        {
            Spacing = 10,
            MinWidth = 380,
            Children =
            {
                new TextBlock
                {
                    Text = _localization.Get("CalendarExport_ChooseMessage"),
                    TextWrapping = TextWrapping.Wrap,
                },
                currentRadio,
                yearRadio,
                singleRadio,
                singleField,
                rangeRadio,
                rangeFields,
            },
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        var scope = singleRadio.IsChecked == true
            ? CalendarExportScope.SingleDate
            : rangeRadio.IsChecked == true
                ? CalendarExportScope.DateRange
                : yearRadio.IsChecked == true
                    ? CalendarExportScope.WholeYear
                    : CalendarExportScope.CurrentMonth;

        // A field the user never touched keeps the initial date, so the choice always carries three
        // usable dates and the caller needs no null handling.
        return new CalendarExportChoice(
            scope,
            DateOnly.FromDateTime(singlePicker.Date?.DateTime ?? initial),
            DateOnly.FromDateTime(fromPicker.Date?.DateTime ?? initial),
            DateOnly.FromDateTime(toPicker.Date?.DateTime ?? initial));
    }

    /// <inheritdoc />
    public async Task<DateOnly?> PickDateAsync(string titleResourceKey, DateOnly initialValue)
    {
        var picker = new CalendarDatePicker
        {
            Date = initialValue.ToDateTime(TimeOnly.MinValue),
            MinWidth = 320,
        };

        var dialog = BuildDialog(titleResourceKey, "Common_ChooseDate", null);
        dialog.Content = picker;
        dialog.PrimaryButtonText = _localization.Get("Common_Ok");
        dialog.CloseButtonText = _localization.Get("Common_Cancel");

        return await dialog.ShowAsync() == ContentDialogResult.Primary && picker.Date is { } selected
            ? DateOnly.FromDateTime(selected.DateTime)
            : null;
    }

    /// <inheritdoc />
    public async Task<string?> PromptTextAsync(string titleResourceKey, string initialValue)
    {
        var input = new TextBox
        {
            Text = initialValue ?? string.Empty,
            MinWidth = 320,
            SelectionStart = initialValue?.Length ?? 0,
        };

        var dialog = BuildDialog(titleResourceKey, "Common_EnterName", null);
        dialog.Content = input;
        dialog.PrimaryButtonText = _localization.Get("Common_Save");
        dialog.CloseButtonText = _localization.Get("Common_Cancel");

        return await dialog.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text)
            ? input.Text.Trim()
            : null;
    }

    private ContentDialog BuildDialog(string titleResourceKey, string messageResourceKey, string? detail)
    {
        var content = new TextBlock
        {
            Text = _localization.Get(messageResourceKey),
            TextWrapping = TextWrapping.Wrap,
        };

        if (!string.IsNullOrEmpty(detail))
        {
            content.Text += Environment.NewLine + Environment.NewLine + detail;
        }

        return new ContentDialog
        {
            Title = _localization.Get(titleResourceKey),
            Content = content,
            XamlRoot = App.MainAppWindow?.Content.XamlRoot,
        };
    }
}
