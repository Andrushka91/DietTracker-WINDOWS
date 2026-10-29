using System.Collections.ObjectModel;
using System.ComponentModel;
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

    /// <summary>Number of confirmed meals of the selected day (the green square).</summary>
    [ObservableProperty]
    private int _doneCount;

    /// <summary>Number of missed meals of the selected day (the red square). Missed is derived at
    /// read time: recorded, unconfirmed and past the planned time.</summary>
    [ObservableProperty]
    private int _missedCount;

    /// <summary>Number of pending meals of the selected day (the gray square) - the untouched
    /// placeholders and the meals whose planned time has not passed yet.</summary>
    [ObservableProperty]
    private int _pendingCount;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>True when the selected date is today.</summary>
    public bool IsToday => SelectedDate == _clock.Today;

    public bool IsFutureDate => SelectedDate > _clock.Today;

    public string PreviousDayLabel => _localization.Get("Dashboard_PreviousDay");

    public string NextDayLabel => _localization.Get("Dashboard_NextDay");

    public string TodayLabel => _localization.Get("Common_Today");

    /// <summary>
    /// Loads (or reloads) the dashboard. Called by the page when it is navigated to. A
    /// <see cref="DateOnly"/> parameter (coming from the calendar) selects that day, so the cards
    /// open with the date that was clicked instead of with today.
    /// </summary>
    public async Task InitializeAsync(object? parameter = null)
    {
        switch (parameter)
        {
            case DateOnly date:
                SelectedDate = date;
                break;

            case Guid mealEntryId:
                var today = await _tracking.GetDayAsync(_clock.Today);
                var match = today.FirstOrDefault(e => e.Id == mealEntryId);
                if (match is not null)
                {
                    SelectedDate = match.Date;
                }

                break;
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

            // The cards of the previous day are dropped with their listeners: the page follows the
            // inline editors of the cards it currently shows (CanSaveAll), and a stale listener
            // would raise that change for a card nobody can see any more.
            foreach (var previous in Cards)
            {
                previous.PropertyChanged -= OnCardPropertyChanged;
            }

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
                card.PropertyChanged += OnCardPropertyChanged;
                Cards.Add(card);
            }

            Percentage = progress.Percentage;
            DoneCount = progress.Completed;
            MissedCount = progress.Missed;
            PendingCount = progress.Pending;

            DateLabel = SelectedDate.ToDateTime(TimeOnly.MinValue).ToString("D");

            OnPropertyChanged(nameof(IsToday));
            OnPropertyChanged(nameof(IsFutureDate));
        }
        finally
        {
            IsLoading = false;

            // The two day-wide buttons (mark all done / mark all missed) enable and disable with the cards.
            OnPropertyChanged(nameof(CanMarkAllDone));
            OnPropertyChanged(nameof(CanMarkAllMissed));

            // A reloaded day never carries an open editor, so the "Save all" button settles with it.
            OnPropertyChanged(nameof(CanSaveAll));
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
