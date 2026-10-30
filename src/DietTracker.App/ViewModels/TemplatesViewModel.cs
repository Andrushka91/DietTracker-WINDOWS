using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.App.ViewModels;

/// <summary>One reusable template shown in the list.</summary>
public sealed class TemplateItemViewModel
{
    public TemplateItemViewModel(MealTemplate template, string kindLabel, string summary)
    {
        Template = template;
        KindLabel = kindLabel;
        Summary = summary;
    }

    public MealTemplate Template { get; }

    public Guid Id => Template.Id;

    public string Name => Template.Name;

    public string KindLabel { get; }

    public string Summary { get; }

    public string UpdatedText => Template.UpdatedAt.ToLocalTime().ToString("g");
}

/// <summary>
/// Templates screen: reusable single meals and full days, and applying them to a date.
/// Templates never carry completion state or photographs.
/// </summary>
public sealed partial class TemplatesViewModel : ObservableObject
{
    private readonly IMealTrackingService _tracking;
    private readonly IDialogService _dialogs;
    private readonly ILocalizationService _localization;
    private readonly IClock _clock;

    public TemplatesViewModel(
        IMealTrackingService tracking,
        IDialogService dialogs,
        ILocalizationService localization,
        IClock clock)
    {
        _tracking = tracking;
        _dialogs = dialogs;
        _localization = localization;
        _clock = clock;
    }

    public ObservableCollection<TemplateItemViewModel> Templates { get; } = [];

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isEmpty = true;

    public string EmptyText => _localization.Get("Template_Empty");

    public string ApplyLabel => _localization.Get("Template_Apply");

    /// <summary>Loads the templates of the active profile.</summary>
    public async Task InitializeAsync(object? parameter = null) => await LoadAsync();

    [RelayCommand]
    private async Task LoadAsync()
    {
        var templates = await _tracking.GetTemplatesAsync();

        Templates.Clear();
        foreach (var template in templates.OrderByDescending(t => t.UpdatedAt))
        {
            var kind = template.IsFullDay ? _localization.Get("Template_KindDay") : _localization.Get("Template_KindMeal");
            var summary = template.IsFullDay
                ? _localization.Format("Template_DaySummary", template.TemplateDay.Count)
                : template.TemplateMeal?.Title ?? template.TemplateMeal?.Ingredients ?? string.Empty;

            Templates.Add(new TemplateItemViewModel(template, kind, summary));
        }

        IsEmpty = Templates.Count == 0;
    }

    [RelayCommand]
    private async Task ApplyAsync(TemplateItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var target = await _dialogs.PickDateAsync("Template_ApplyTitle", _clock.Today);
        if (target is null)
        {
            return;
        }

        await _tracking.ApplyTemplateAsync(item.Id, target.Value);
        StatusMessage = _localization.Format("Template_Applied", target.Value.ToString("d"));
    }
}
