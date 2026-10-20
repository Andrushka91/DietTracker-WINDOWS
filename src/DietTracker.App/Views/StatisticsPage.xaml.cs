using DietTracker.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DietTracker.App.Views;

/// <summary>Progress and statistics: daily, weekly and monthly completion plus the trend series.</summary>
public sealed partial class StatisticsPage : Page
{
    public StatisticsPage()
    {
        ViewModel = App.Services.GetRequiredService<StatisticsViewModel>();
        InitializeComponent();
    }

    /// <summary>View model of the page (exposed for <c>x:Bind</c>).</summary>
    public StatisticsViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // This is an async void handler: an exception that escapes it terminates the process, so the
        // load is guarded - when the data cannot be read the page keeps its previous state instead.
        try
        {
            await ViewModel.InitializeAsync(e.Parameter);
        }
        catch (Exception ex)
        {
            App.Services.GetService<ILogger<StatisticsPage>>()
                ?.LogWarning(ex, "The statistics could not be loaded.");
        }
    }
}
