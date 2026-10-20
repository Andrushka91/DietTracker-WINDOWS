using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DietTracker.Core.Enums;
using DietTracker.Core.Models;

namespace DietTracker.App.ViewModels;

/// <summary>
/// View model of a single permanent meal card. Supports inline editing, DONE/PENDING/MISSED
/// transitions, photographs, copying to another date and saving as a template.
/// </summary>
public sealed partial class MealCardViewModel : ObservableObject
{
    private readonly MealEntry _entry;
    private readonly MealCategory _category;
    private readonly IMealCardHost _host;

    public MealCardViewModel(MealEntry entry, MealCategory category, IMealCardHost host)
    {
        _entry = entry;
        _category = category;
        _host = host;

        Id = entry.Id;
        Date = entry.Date;
        CategoryKey = entry.MealCategoryKey;
        Color = string.IsNullOrWhiteSpace(category.Color) ? "#FF0F6CBD" : category.Color;
        Icon = string.IsNullOrWhiteSpace(category.Icon) ? "\uE7C1" : category.Icon;

        Title = entry.Title;
        Ingredients = entry.Ingredients;
        Notes = entry.Notes;
        PlannedTime = entry.PlannedTime;
        Status = entry.Status;
        CompletedAt = entry.CompletedAt;

        foreach (var line in entry.Quantities)
        {
            Quantities.Add(new IngredientLineViewModel(line, RemoveIngredientLine));
        }
    }

    /// <summary>Localized display name of the permanent category (never editable, never deletable).</summary>
    public string CategoryName => _host.Localize($"Meal_{CategoryKey}_Name");

    public Guid Id { get; }

    public DateOnly Date { get; }

    public MealCategoryKey CategoryKey { get; }

    public string Color { get; }

    public string Icon { get; }

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _ingredients = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

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

    public ObservableCollection<IngredientLineViewModel> Quantities { get; } = [];

    public ObservableCollection<MealPhotoViewModel> Photos { get; } = [];

    /// <summary>Status label shown in the pill.</summary>
    public string StatusText => _host.Localize($"Status_{Status}");

    public string CompletedAtText => CompletedAt is { } timestamp
        ? $"{_host.Localize("Meal_CompletedAt")} {timestamp.ToLocalTime():g}"
        : string.Empty;

    /// <summary>Planned time shown on the card; falls back to the category default time.</summary>
    public TimeOnly EffectivePlannedTime => PlannedTime ?? _category.DefaultTime;

    /// <summary>True when the meal may be marked as missed (today or earlier, never a future date).</summary>
    public bool CanMarkMissed => Date <= _host.Today && Status != MealStatus.Missed;

    public bool CanUndo => Status != MealStatus.Pending;

    public bool HasPhotos => Photos.Count > 0;

    public bool CanAddPhoto => Photos.Count < Core.BusinessRules.PhotoRules.MaxPhotosPerMeal;

    public string PhotoLimitText => $"{Photos.Count}/{Core.BusinessRules.PhotoRules.MaxPhotosPerMeal}";

    /// <summary>Time editor value; <see cref="Microsoft.UI.Xaml.Controls.TimePicker"/> binds a TimeSpan.</summary>
    public TimeSpan PlannedTimeEditor
    {
        get => (PlannedTime ?? _category.DefaultTime).ToTimeSpan();
        set
        {
            PlannedTime = TimeOnly.FromTimeSpan(value);
            OnPropertyChanged();
        }
    }

    partial void OnStatusChanged(MealStatus value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanMarkMissed));
    }

    partial void OnCompletedAtChanged(DateTimeOffset? value) => OnPropertyChanged(nameof(CompletedAtText));

    /// <summary>True when the last save attempt reported a validation problem.</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    /// <summary>Copies the editable values back into the domain entry before it is persisted.</summary>
    public void ApplyEditsTo(MealEntry target)
    {
        ArgumentNullException.ThrowIfNull(target);

        target.Title = Title;
        target.Ingredients = Ingredients;
        target.Notes = Notes;
        target.PlannedTime = PlannedTime;
        target.Quantities = Quantities.Select(q => q.ToModel()).Where(q => q.Name.Length > 0).ToList();
    }

    /// <summary>Reloads the card from a persisted entry (used after a save or a copy).</summary>
    public void Reload(MealEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Title = entry.Title;
        Ingredients = entry.Ingredients;
        Notes = entry.Notes;
        PlannedTime = entry.PlannedTime;
        Status = entry.Status;
        CompletedAt = entry.CompletedAt;

        Quantities.Clear();
        foreach (var line in entry.Quantities)
        {
            Quantities.Add(new IngredientLineViewModel(line, RemoveIngredientLine));
        }

        IsEditing = false;
        ErrorMessage = null;
    }

    private void RemoveIngredientLine(IngredientLineViewModel line) => Quantities.Remove(line);

    /// <summary>Raises change notifications for the properties derived from the photo collection.</summary>
    public void NotifyPhotoStateChanged()
    {
        OnPropertyChanged(nameof(Photos));
        OnPropertyChanged(nameof(HasPhotos));
        OnPropertyChanged(nameof(CanAddPhoto));
        OnPropertyChanged(nameof(PhotoLimitText));
    }

    /// <summary>
    /// Compact, read-only rendering of the ingredient lines ("Oat flakes 60 g, Milk 200 ml").
    /// Avoids a nested data template in the card while keeping quantities and units visible.
    /// </summary>
    public string QuantitiesSummary => string.Join(
        ", ",
        Quantities
            .Where(q => !string.IsNullOrWhiteSpace(q.Name))
            .Select(q => string.Join(
                " ",
                new[] { q.Name, q.Amount, IngredientLineViewModel.LocalizeUnit(q.Unit) }.Where(part => !string.IsNullOrWhiteSpace(part)))));
}