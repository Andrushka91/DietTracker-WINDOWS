using DietTracker.Core.Interfaces;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace DietTracker.App.Services;

/// <summary>
/// Localization backed by the compiled resource files (MRT Core), so every user visible string lives
/// in <c>Resources\Strings\&lt;language&gt;\Resources.resw</c> and none is hardcoded in XAML or C#.
/// </summary>
public sealed class ResourceLocalizationService : ILocalizationService
{
    private readonly ResourceLoader _loader = new();

    /// <inheritdoc />
    public string CurrentLanguage { get; private set; } = string.Empty;

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
            // UI and in the automated resource parity test.
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

    /// <inheritdoc />
    /// <remarks>
    /// The language override is applied by the Windows resource loader at startup, so a change takes
    /// effect on the next launch; this avoids rebuilding the visual tree while the user is editing.
    /// </remarks>
    public void SetLanguage(string bcp47Tag)
    {
        CurrentLanguage = bcp47Tag ?? string.Empty;

        if (!string.IsNullOrEmpty(bcp47Tag))
        {
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = bcp47Tag;
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
        dialog.CloseButtonText = _localization.Get("Common_Cancel");

        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            ItemsSource = optionResourceKeys.Select(_localization.Get).ToArray(),
            MinWidth = 320,
        };

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
        return result == ContentDialogResult.None ? -1 : list.SelectedIndex;
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
