using DietTracker.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DietTracker.App.Views;

/// <summary>Monthly calendar used to review history and to jump to a specific day.</summary>
public sealed partial class CalendarPage : Page
{
    public CalendarPage()
    {
        ViewModel = App.Services.GetRequiredService<CalendarViewModel>();
        InitializeComponent();
        ViewModel.OpenDayRequested += OnOpenDayRequested;
    }

    /// <summary>View model of the page (exposed for <c>x:Bind</c>).</summary>
    public CalendarViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Guarded like StatisticsPage: an escaping exception in this async void handler would exit the
        // application, so a failed load leaves the page in its previous state instead.
        try
        {
            await ViewModel.InitializeAsync(e.Parameter);
        }
        catch (Exception ex)
        {
            App.Services.GetService<ILogger<CalendarPage>>()
                ?.LogWarning(ex, "The calendar could not be loaded.");
        }
    }

    private void OnDayClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CalendarDayViewModel day })
        {
            _ = ViewModel.SelectDayCommand.ExecuteAsync(day);
        }
    }

    private void OnOpenDayRequested(object? sender, DateOnly date)
    {
        // Opens the dashboard for the selected date; the day is passed as navigation parameter.
        if (App.MainAppWindow is MainWindow shell)
        {
            shell.Navigate("dashboard", date);
        }
    }
}
