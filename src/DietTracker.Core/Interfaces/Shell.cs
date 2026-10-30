namespace DietTracker.Core.Interfaces;

/// <summary>Registers / unregisters Diet Tracker as a Windows startup task.</summary>
public interface IStartupTaskService
{
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);

    Task<bool> TrySetEnabledAsync(bool enabled, CancellationToken cancellationToken = default);
}

/// <summary>Native Windows file pickers (never a web based file dialog).</summary>
public interface IFilePickerService
{
    Task<IReadOnlyList<string>> PickImagesAsync(int maxCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks where to save a file and offers every format of <paramref name="choices"/> in the type
    /// list of the dialog, so one button can export the same data in more than one format (a backup
    /// archive or a printable PDF report, for example).
    /// </summary>
    Task<string?> PickSaveFileAsync(string suggestedFileName, IReadOnlyList<SaveFileChoice> choices, CancellationToken cancellationToken = default);

    Task<string?> PickOpenFileAsync(string extension, CancellationToken cancellationToken = default);

    Task<string?> PickFolderAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens a file with the shell (used for the full-size photo preview).</summary>
    Task OpenWithShellAsync(string path);
}

/// <summary>
/// One entry of the type list of a save dialog: the text the user reads and the extension the file
/// gets (<c>.dtbackup</c>, <c>.pdf</c>). Keeping it in Core lets a view model describe the formats it
/// can write without referencing the Windows pickers.
/// </summary>
/// <param name="Label">Text shown in the type list, already localized.</param>
/// <param name="Extension">Extension including the leading dot.</param>
public sealed record SaveFileChoice(string Label, string Extension);

/// <summary>Reports network availability so that the engine can defer cloud work and show "Offline".</summary>
public interface IConnectivityMonitor
{
    bool IsOnline { get; }

    event EventHandler<bool>? ConnectivityChanged;
}

/// <summary>UI agnostic dialog service so Core services never reference XAML types.</summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(string titleResourceKey, string messageResourceKey, string? detail = null);

    /// <summary>Shows a question whose primary button carries an action label (for example "Edit",
    /// from <paramref name="primaryResourceKey"/>) beside a plain OK dismiss button. Returns true
    /// only when the action button was pressed; OK (the default button) returns false, so the
    /// keyboard and an accidental click always land on the safe answer.</summary>
    Task<bool> OfferAsync(string titleResourceKey, string messageResourceKey, string primaryResourceKey, string? detail = null);

    Task ShowMessageAsync(string titleResourceKey, string messageResourceKey, string? detail = null);

    /// <summary>Asks the user to choose one of the supplied options (resource keys); returns the index.</summary>
    Task<int> ChooseAsync(string titleResourceKey, string messageResourceKey, IReadOnlyList<string> optionResourceKeys);

    /// <summary>
    /// Asks which part of the calendar to export - the current month, every month of the year, the
    /// month of a single date or every month between two dates - with the date fields of the last
    /// two options inside the very same popup (no follow-up dialog). Returns <see langword="null"/>
    /// when the dialog is cancelled; otherwise the chosen scope together with the dates the fields
    /// held (unused fields keep <paramref name="initialDate"/>).
    /// </summary>
    Task<CalendarExportChoice?> ChooseCalendarExportAsync(DateOnly initialDate);

    /// <summary>Asks the user for a calendar date (for example the target of a copy).</summary>
    Task<DateOnly?> PickDateAsync(string titleResourceKey, DateOnly initialValue);

    /// <summary>Asks the user for a short text value (for example a template name).</summary>
    Task<string?> PromptTextAsync(string titleResourceKey, string initialValue);
}

/// <summary>What the calendar export popup offers; decided by the option the user selects.</summary>
public enum CalendarExportScope
{
    /// <summary>The month of today (the preselected option).</summary>
    CurrentMonth,

    /// <summary>Every month of the year of today: one page per month.</summary>
    WholeYear,

    /// <summary>The month that contains the single date of its field.</summary>
    SingleDate,

    /// <summary>Every month between the two dates of its fields, inclusive.</summary>
    DateRange,
}

/// <summary>
/// Result of the calendar export popup: the selected scope plus the content of the date fields.
/// The dates always carry a value (the dialog fills them with its initial date), so the caller can
/// read the fields it needs for the scope without null handling.
/// </summary>
/// <param name="Scope">The option the user selected.</param>
/// <param name="SingleDate">Content of the single date field.</param>
/// <param name="RangeStart">First date of the range field (the caller normalizes the order).</param>
/// <param name="RangeEnd">Second date of the range field (the caller normalizes the order).</param>
public sealed record CalendarExportChoice(
    CalendarExportScope Scope,
    DateOnly SingleDate,
    DateOnly RangeStart,
    DateOnly RangeEnd);

/// <summary>
/// Localized string lookup backed by the application resource files. Diet Tracker ships in English
/// only: en-US is forced at start-up and the resource service simply resolves keys from that file.
/// </summary>
public interface ILocalizationService
{
    /// <summary>Resolves a resource key.</summary>
    string Get(string resourceKey);

    /// <summary>Resolves a resource key and formats it with the supplied arguments.</summary>
    string Format(string resourceKey, params object?[] args);
}

/// <summary>Keeps the application alive in the notification area and owns window lifetime decisions.</summary>
public interface ITrayIconService
{
    Task ShowAsync(CancellationToken cancellationToken = default);

    Task HideAsync(CancellationToken cancellationToken = default);

    Task ShowBalloonAsync(string title, string message, CancellationToken cancellationToken = default);

    event EventHandler? RestoreRequested;

    event EventHandler? ExitRequested;
}

/// <summary>Exposes build/runtime information for the About and diagnostics screens.</summary>
public interface IAppEnvironment
{
    string ApplicationVersion { get; }

    string PackageFamilyName { get; }

    bool IsPackaged { get; }

    string DeviceId { get; }

    string OperatingSystemDescription { get; }
}
