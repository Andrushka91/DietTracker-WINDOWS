using DietTracker.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace DietTracker.App.Views;

/// <summary>
/// First-launch welcome screen. Signing in is optional: every essential feature works in Guest Mode,
/// and the account can always be connected again from the settings page.
/// </summary>
public sealed partial class OnboardingPage : Page
{
    public OnboardingPage()
    {
        ViewModel = App.Services.GetRequiredService<OnboardingViewModel>();
        InitializeComponent();

        ViewModel.Completed += OnCompleted;
    }

    /// <summary>View model of the page (exposed for <c>x:Bind</c>).</summary>
    public OnboardingViewModel ViewModel { get; }

    private async void OnCompleted(object? sender, EventArgs e)
    {
        ViewModel.Completed -= OnCompleted;

        // Remember that the welcome screen is done, then continue to the dashboard.
        await App.Services.GetRequiredService<ShellViewModel>().CompleteOnboardingAsync();

        if (App.MainAppWindow is MainWindow shell)
        {
            shell.Navigate("dashboard");
        }
    }
}
