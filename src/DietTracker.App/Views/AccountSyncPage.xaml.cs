using DietTracker.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DietTracker.App.Views;

/// <summary>Account and synchronization: provider selection, state, restore and switching.</summary>
public sealed partial class AccountSyncPage : Page
{
    public AccountSyncPage()
    {
        ViewModel = App.Services.GetRequiredService<AccountSyncViewModel>();
        InitializeComponent();
    }

    /// <summary>View model of the page (exposed for <c>x:Bind</c>).</summary>
    public AccountSyncViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.InitializeAsync(e.Parameter);
    }

    private void OnSwitch(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string provider })
        {
            _ = ViewModel.SwitchProviderCommand.ExecuteAsync(provider);
        }
    }
}
