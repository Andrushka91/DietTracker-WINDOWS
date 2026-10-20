using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;

namespace DietTracker.App.ViewModels;

/// <summary>
/// Daily dashboard: the five permanent meal cards, the daily progress indicator and the day level
/// actions (copy the previous day, save the day as a template).
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject, IMealCardHost
{
    private readonly IMealTrackingService _tracking;
    private readonly IPhotoStore _photoStore;
    private readonly IFilePickerService _filePicker;
    private readonly IDialogService _dialogs;
    private readonly ILocalizationService _localization;
    private readonly IClock _clock;
    private readonly ISyncEngine _syncEngine;

    public DashboardViewModel(
        IMealTrackingService tracking,
        IPhotoStore photoStore,
        IFilePickerService filePicker,
        IDialogService dialogs,
        ILocalizationService localization,
        IClock clock,
        ISyncEngine syncEngine)
    {
        _tracking = tracking;
        _photoStore = photoStore;
        _filePicker = filePicker;
        _dialogs = dialogs;
        _localization = localization;
        _clock = clock;
        _syncEngine = syncEngine;

        _selectedDate = clock.Today;
        _dateLabel = string.Empty;
    }

    /// <summary>The five permanent cards, always in canonical order.</summary>
    public ObservableCollection<MealCardViewModel> Cards { get; } = [];

    [ObservableProperty]
    private DateOnly _selectedDate;

    [ObservableProperty]
    private string _dateLabel = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private double _percentage;

    [ObservableProperty]
    private int _completedCount;

    [ObservableProperty]
    private int _pendingCount;

    [ObservableProperty]
    private int _missedCount;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>True when the selected date is today.</summary>
    public bool IsToday => SelectedDate == _clock.Today;

    public bool IsFutureDate => SelectedDate > _clock.Today;

    public string PreviousDayLabel => _localization.Get("Dashboard_PreviousDay");

    public string NextDayLabel => _localization.Get("Dashboard_NextDay");

    public string TodayLabel => _localization.Get("Common_Today");

    public string CompletedLabel => _localization.Get("Dashboard_Completed");

    public string PendingLabel => _localization.Get("Dashboard_Pending");

    public string MissedLabel => _localization.Get("Dashboard_Missed");

    public string CopyYesterdayLabel => _localization.Get("Dashboard_CopyYesterday");

    public string SaveDayTemplateLabel => _localization.Get("Dashboard_SaveDayTemplate");

    /// <summary>Loads (or reloads) the dashboard. Called by the page when it is navigated to.</summary>
    public async Task InitializeAsync(object? parameter = null)
    {
        if (parameter is Guid mealEntryId)
        {
            var today = await _tracking.GetDayAsync(_clock.Today);
            var match = today.FirstOrDefault(e => e.Id == mealEntryId);
            if (match is not null)
            {
                SelectedDate = match.Date;
            }
        }

        await LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var categories = await _tracking.GetCategoriesAsync(cancellationToken);
            var meals = await _tracking.GetDayAsync(SelectedDate, cancellationToken);
            var progress = await _tracking.GetDayProgressAsync(SelectedDate, cancellationToken);

            Cards.Clear();
            foreach (var category in categories.OrderBy(c => c.FixedCategoryKey))
            {
                var entry = meals.FirstOrDefault(m => m.MealCategoryKey == category.FixedCategoryKey);
                if (entry is null)
                {
                    continue;
                }

                var card = new MealCardViewModel(entry, category, this);
                await AttachPhotosAsync(card, cancellationToken);
                Cards.Add(card);
            }

            Percentage = progress.Percentage;
            CompletedCount = progress.Completed;
            PendingCount = progress.Pending;
            MissedCount = progress.Missed;
            DateLabel = SelectedDate.ToDateTime(TimeOnly.MinValue).ToString("D");
            SummaryText = _localization.Format("Dashboard_Summary", progress.Completed, progress.TotalSlots, progress.Percentage);
            OnPropertyChanged(nameof(IsToday));
            OnPropertyChanged(nameof(IsFutureDate));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Reloads the photographs of one card from the local photo store.</summary>
    private async Task AttachPhotosAsync(MealCardViewModel card, CancellationToken cancellationToken)
    {
        var photos = await _tracking.GetPhotosAsync(card.Id, cancellationToken);
        card.Photos.Clear();

        foreach (var photo in photos)
        {
            card.Photos.Add(new MealPhotoViewModel(
                photo,
                _photoStore.GetThumbnailPath(photo.ThumbnailId),
                _photoStore.GetFullSizePath(photo.LocalFileId),
                _photoStore.GetFullSizePath(photo.LocalFileId)));
        }

        card.NotifyPhotoStateChanged();
    }
}
