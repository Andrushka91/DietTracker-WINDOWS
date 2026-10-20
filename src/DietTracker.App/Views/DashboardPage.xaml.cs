using DietTracker.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DietTracker.App.Views;

/// <summary>Daily dashboard: the five permanent meal cards and the daily progress.</summary>
public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        ViewModel = App.Services.GetRequiredService<DashboardViewModel>();
        InitializeComponent();
    }

    /// <summary>View model of the page (exposed for <c>x:Bind</c>).</summary>
    public DashboardViewModel ViewModel { get; }

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
            App.Services.GetService<ILogger<DashboardPage>>()
                ?.LogWarning(ex, "The dashboard could not be loaded.");
        }
    }
}
