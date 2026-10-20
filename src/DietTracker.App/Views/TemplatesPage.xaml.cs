using DietTracker.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DietTracker.App.Views;

/// <summary>Reusable single-meal and full-day templates.</summary>
public sealed partial class TemplatesPage : Page
{
    public TemplatesPage()
    {
        ViewModel = App.Services.GetRequiredService<TemplatesViewModel>();
        InitializeComponent();
    }

    /// <summary>View model of the page (exposed for <c>x:Bind</c>).</summary>
    public TemplatesViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.InitializeAsync(e.Parameter);
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TemplateItemViewModel item })
        {
            _ = ViewModel.ApplyCommand.ExecuteAsync(item);
        }
    }
}
