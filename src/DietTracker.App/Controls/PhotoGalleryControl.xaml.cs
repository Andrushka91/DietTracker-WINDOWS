using DietTracker.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DietTracker.App.Controls;

/// <summary>
/// The photograph gallery of a meal card: a framed, wrapping grid of square thumbnail tiles. It is a
/// read-only view over <see cref="MealCardViewModel.Photos"/> - every interaction (open full size,
/// remove) is routed through the card's commands, so the gallery never touches persistence or files.
/// </summary>
public sealed partial class PhotoGalleryControl : UserControl
{
    /// <summary>Identifies the <see cref="Card"/> dependency property.</summary>
    public static readonly DependencyProperty CardProperty = DependencyProperty.Register(
        nameof(Card),
        typeof(MealCardViewModel),
        typeof(PhotoGalleryControl),
        new PropertyMetadata(null));

    public PhotoGalleryControl() => InitializeComponent();

    /// <summary>The card whose photographs are displayed.</summary>
    public MealCardViewModel? Card
    {
        get => (MealCardViewModel?)GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }

    /// <summary>Opens the tapped photograph full size through the shell.</summary>
    private void OnOpenPhoto(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MealPhotoViewModel photo })
        {
            _ = Card?.OpenPhotoCommand.ExecuteAsync(photo);
        }
    }

    /// <summary>Removes the photograph whose X badge was tapped; the host asks for confirmation.</summary>
    private void OnDeletePhoto(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MealPhotoViewModel photo })
        {
            _ = Card?.RemovePhotoCommand.ExecuteAsync(photo);
        }
    }
}
