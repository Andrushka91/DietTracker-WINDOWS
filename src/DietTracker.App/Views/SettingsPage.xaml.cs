using DietTracker.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DietTracker.App.Views;

/// <summary>
/// Application settings: the connected account, synchronization and restore, language, reminders,
/// Windows integration, backups and privacy - one page for everything that is configured instead of
/// a separate account page.
/// </summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        InitializeComponent();

        // The window height is fixed to the dashboard, so the settings page claims the whole
        // viewport: the ScrollViewer handler runs inside the layout pass (the panels are born at
        // their final height) and the dispatcher handler covers the later changes (window or
        // content size, guest info bar opening).
        ScrollHost.SizeChanged += (_, _) => UpdateContentHeight();
        SizeChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateContentHeight);

        // The account photo is only known by its URL: when it cannot be downloaded (offline, address
        // no longer served), the empty URL puts the framed glyph back instead of leaving an empty tile.
        AccountPhotoImage.ImageFailed += (_, _) => ViewModel.AccountPhotoUrl = string.Empty;
    }

    /// <summary>View model of the page (exposed for <c>x:Bind</c>).</summary>
    public SettingsViewModel ViewModel { get; }

    /// <summary>
    /// Keeps the layout as tall as the viewport so the settings cards cover the whole fixed height
    /// of the window: the MinHeight of the root grid becomes the viewport, and because the grid's
    /// card rows are starred they share everything the fixed content (title, guest info, the natural
    /// height of the cards) leaves over. When the content is naturally taller than the viewport the
    /// desired size wins and the ScrollViewer scrolls as before, so nothing is ever clipped.
    /// </summary>
    private void UpdateContentHeight()
    {
        var viewport = ScrollHost.ActualHeight - ScrollHost.Padding.Top - ScrollHost.Padding.Bottom;
        if (viewport <= 0d)
        {
            return; // Not laid out yet.
        }

        // The half-pixel tolerance keeps the layout from oscillating on rounding noise.
        if (Math.Abs(RootLayout.MinHeight - viewport) > 0.5d)
        {
            RootLayout.MinHeight = viewport;
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.InitializeAsync(e.Parameter);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        // The engine outlives the page, so the subscription has to be removed explicitly.
        ViewModel.Detach();
    }
}
