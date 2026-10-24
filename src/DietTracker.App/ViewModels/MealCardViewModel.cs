using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DietTracker.Core.BusinessRules;
using DietTracker.Core.Enums;
using DietTracker.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace DietTracker.App.ViewModels;

/// <summary>
/// View model of a single permanent meal card. Supports inline editing, DONE/MISSED/PENDING
/// transitions (today and past dates only - future meals stay pending), photographs, copying to
/// another date and saving as a template.
/// </summary>
public sealed partial class MealCardViewModel : ObservableObject
{
    private readonly MealEntry _entry;
    private readonly MealCategory _category;
    private readonly IMealCardHost _host;

    /// <summary>Font of the food emoji defaults (the Segoe Fluent icon font has no food glyphs).</summary>
    private static readonly FontFamily EmojiIconFont = new("Segoe UI Emoji");

    /// <summary>Font of every other icon glyph.</summary>
    private static readonly FontFamily SymbolIconFont = new("Segoe Fluent Icons");

    public MealCardViewModel(MealEntry entry, MealCategory category, IMealCardHost host)
    {
        _entry = entry;
        _category = category;
        _host = host;

        Id = entry.Id;
        Date = entry.Date;
        CategoryKey = entry.MealCategoryKey;

        // A category without an icon falls back to the permanent default of that slot (a croissant,
        // a cookie, a plate or a pot) instead of an unrelated symbol glyph, so an empty Icon column
        // never shows a house pictogram.
        Icon = string.IsNullOrWhiteSpace(category.Icon) ? DefaultIconOf(entry.MealCategoryKey) : category.Icon;

        // An emoji (plate, cookie) starts with a surrogate pair, not with a code point above
        // U+FFFF - checking the first char alone would always pick the symbol font for them.
        IconFontFamily = char.IsSurrogatePair(Icon, 0) ? EmojiIconFont : SymbolIconFont;

        Title = entry.Title;
        Ingredients = entry.Ingredients;
        PlannedTime = entry.PlannedTime;
        PlannedTimeText = FormatPlannedTime(entry.PlannedTime);
        Status = entry.Status;
        CompletedAt = entry.CompletedAt;
    }

    /// <summary>Localized display name of the permanent category (never editable, never deletable).</summary>
    public string CategoryName => _host.Localize($"Meal_{CategoryKey}_Name");

    public Guid Id { get; }

    public DateOnly Date { get; }

    public MealCategoryKey CategoryKey { get; }

    public string Icon { get; }

    /// <summary>
    /// Font used to paint <see cref="Icon"/>: emoji glyphs (the plate and the cookie) need the
    /// emoji font, every other glyph uses the symbol font of the design language.
    /// </summary>
    public FontFamily IconFontFamily { get; }

    /// <summary>The permanent default pictogram of one slot (croissant, cookie, plate, pot).</summary>
    private static string DefaultIconOf(MealCategoryKey key) => MealSlotRules.CreateDefaults()
        .First(c => c.FixedCategoryKey == key)
        .Icon;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _ingredients = string.Empty;

    /// <summary>Typed planned time ("HH:MM"); empty means the category default time is used.</summary>
    [ObservableProperty]
    private string _plannedTimeText = string.Empty;

    [ObservableProperty]
    private TimeOnly? _plannedTime;

    [ObservableProperty]
    private MealStatus _status = MealStatus.Pending;

    [ObservableProperty]
    private DateTimeOffset? _completedAt;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<MealPhotoViewModel> Photos { get; } = [];

    /// <summary>
    /// Status label shown in the pill: "Done" once the meal is confirmed, "Missed" once the user
    /// marked it as not eaten, "Pending" otherwise - pending being the default state of a meal the
    /// user has not decided on yet.
    /// </summary>
    public string StatusText => _host.Localize(StatusKey(Status));

    /// <summary>The three state colours: green for DONE, red for MISSED and gray for PENDING - the
    /// initial state every untouched meal keeps. They are shared with the dashboard counters, the
    /// calendar cells and the statistics bars so one state always carries one colour.</summary>
    public static readonly Brush DoneBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 16));
    public static readonly Brush MissedBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 164, 38, 44));
    public static readonly Brush PendingBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 96, 94, 92));

    /// <summary>Resource key of the state label of one meal slot.</summary>
    public static string StatusKey(MealStatus status)
        => status switch
        {
            MealStatus.Done => "Status_Done",
            MealStatus.Missed => "Status_Missed",
            _ => "Status_Pending",
        };

    /// <summary>Colour of the card frame: green when confirmed, red when missed, gray while pending.</summary>
    public Brush CardBorderBrush
        => Status switch
        {
            MealStatus.Done => DoneBrush,
            MealStatus.Missed => MissedBrush,
            _ => PendingBrush,
        };

    /// <summary>Background of the status pill (the keyword itself); the same three colours as the frame.</summary>
    public Brush StatusPillBrush => CardBorderBrush;

    /// <summary>Glyph of the status pill: check (done), cross (missed) or clock (pending).</summary>
    public string StatusGlyph
        => Status switch
        {
            MealStatus.Done => "\uE73E",
            MealStatus.Missed => "\uE711",
            _ => "\uE823",
        };

    public string CompletedAtText => CompletedAt is { } timestamp
        ? $"{_host.Localize("Meal_CompletedAt")} {timestamp.ToLocalTime():g}"
        : string.Empty;

    /// <summary>Planned time shown on the card; falls back to the category default time.</summary>
    public TimeOnly EffectivePlannedTime => PlannedTime ?? _category.DefaultTime;

    /// <summary>True when the meal's date allows state changes: today and past dates are fully
    /// editable, a future meal is locked in pending no matter how often the buttons are pressed.</summary>
    private bool CanChangeState => Date <= _host.Today;

    /// <summary>True when the meal can still be confirmed as eaten (today or a past date only,
    /// and only while it is not already done).</summary>
    public bool CanMarkDone => CanChangeState && Status != MealStatus.Done;

    /// <summary>True when the meal can still be marked as missed (today or a past date only).
    /// MISSED is sticky, so a missed meal never enables the cross again.</summary>
    public bool CanMarkMissed => CanChangeState && Status != MealStatus.Missed;

    public bool HasPhotos => Photos.Count > 0;

    public bool CanAddPhoto => Photos.Count < Core.BusinessRules.PhotoRules.MaxPhotosPerMeal;

    public string PhotoLimitText => $"{Photos.Count}/{Core.BusinessRules.PhotoRules.MaxPhotosPerMeal}";

    /// <summary>
    /// Parses the typed planned time. The text box keeps the "HH:MM" shape (24 hour, two digits per
    /// part), so anything else - including a partial input - is rejected.
    /// </summary>
    public static bool TryParsePlannedTime(string? text, out TimeOnly plannedTime)
    {
        plannedTime = default;

        if (text is not { Length: 5 } || text[2] != ':'
            || !char.IsAsciiDigit(text[0]) || !char.IsAsciiDigit(text[1])
            || !char.IsAsciiDigit(text[3]) || !char.IsAsciiDigit(text[4]))
        {
            return false;
        }

        var hour = ((text[0] - '0') * 10) + (text[1] - '0');
        var minute = ((text[3] - '0') * 10) + (text[4] - '0');
        if (hour > 23 || minute > 59)
        {
            return false;
        }

        plannedTime = new TimeOnly(hour, minute);
        return true;
    }

    /// <summary>Renders a planned time as invariant "HH:MM"; null becomes an empty input.</summary>
    public static string FormatPlannedTime(TimeOnly? plannedTime)
        => plannedTime?.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    partial void OnStatusChanged(MealStatus value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(CardBorderBrush));
        OnPropertyChanged(nameof(StatusPillBrush));
        OnPropertyChanged(nameof(CanMarkMissed));
        OnPropertyChanged(nameof(CanMarkDone));
    }

    partial void OnCompletedAtChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(CompletedAtText));

    /// <summary>True when the last save attempt reported a validation problem.</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    /// <summary>Copies the editable values back into the domain entry before it is persisted.</summary>
    /// <returns>
    /// <see langword="false"/> when the typed planned time is incomplete or out of range; the error
    /// message is set and nothing may be persisted in that case.
    /// </returns>
    public bool ApplyEditsTo(MealEntry target)
    {
        ArgumentNullException.ThrowIfNull(target);

        target.Title = Title;
        target.Ingredients = Ingredients;

        if (string.IsNullOrWhiteSpace(PlannedTimeText))
        {
            // An empty input falls back to the category default time.
            target.PlannedTime = null;
        }
        else if (TryParsePlannedTime(PlannedTimeText.Trim(), out var plannedTime))
        {
            target.PlannedTime = plannedTime;
        }
        else
        {
            ErrorMessage = _host.Localize("Meal_InvalidTime");
            return false;
        }

        return true;
    }

    /// <summary>Reloads the card from a persisted entry (used after a save or a copy).</summary>
    public void Reload(MealEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Title = entry.Title;
        Ingredients = entry.Ingredients;
        PlannedTime = entry.PlannedTime;
        PlannedTimeText = FormatPlannedTime(entry.PlannedTime);
        Status = entry.Status;
        CompletedAt = entry.CompletedAt;

        IsEditing = false;
        ErrorMessage = null;
    }

    /// <summary>Raises change notifications for the properties derived from the photo collection.</summary>
    public void NotifyPhotoStateChanged()
    {
        OnPropertyChanged(nameof(Photos));
        OnPropertyChanged(nameof(HasPhotos));
        OnPropertyChanged(nameof(CanAddPhoto));
        OnPropertyChanged(nameof(PhotoLimitText));
    }
}