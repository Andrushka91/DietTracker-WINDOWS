using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DietTracker.Core.Enums;
using DietTracker.Core.Models;

namespace DietTracker.App.ViewModels;

/// <summary>One editable ingredient row (name, amount, unit) shown inside a meal card while editing.</summary>
public sealed partial class IngredientLineViewModel : ObservableObject
{
    private readonly Action<IngredientLineViewModel> _remove;

    public IngredientLineViewModel(IngredientLine source, Action<IngredientLineViewModel> remove)
    {
        _remove = remove;
        Name = source.Name;
        Amount = source.Amount?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
        Unit = source.Unit;
        Note = source.Note ?? string.Empty;
    }

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _amount = string.Empty;

    [ObservableProperty]
    private MeasurementUnit _unit = MeasurementUnit.None;

    [ObservableProperty]
    private string _note = string.Empty;

    /// <summary>Units offered by the editor.</summary>
    public static IReadOnlyList<MeasurementUnit> AvailableUnits { get; } = Enum.GetValues<MeasurementUnit>();

    /// <summary>Converts the row back into a domain ingredient line.</summary>
    public IngredientLine ToModel() => new()
    {
        Name = Name.Trim(),
        Amount = decimal.TryParse(Amount, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out var amount)
            ? amount
            : null,
        Unit = Unit,
        Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
    };

    public void Remove() => _remove(this);
}

/// <summary>A photograph attached to a meal, together with its local file paths.</summary>
public sealed class MealPhotoViewModel
{
    public MealPhotoViewModel(MealPhoto photo, string? thumbnailPath, string? fullSizePath, string? localPath)
    {
        Photo = photo;
        ThumbnailPath = thumbnailPath;
        FullSizePath = fullSizePath;
        LocalPath = localPath;
    }

    public MealPhoto Photo { get; }

    public Guid Id => Photo.Id;

    public string OriginalFileName => Photo.OriginalFileName;

    /// <summary>Absolute path of the thumbnail used by the card (null when generation failed).</summary>
    public string? ThumbnailPath { get; }

    public string? FullSizePath { get; }

    public string? LocalPath { get; }

    public string SizeText => $"{Photo.FileSize / 1024d:0.#} KB";

    public SyncStatus SyncStatus => Photo.SyncStatus;
}
