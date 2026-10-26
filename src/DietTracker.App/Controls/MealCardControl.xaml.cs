using DietTracker.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DietTracker.App.Controls;

/// <summary>
/// The permanent meal card: reads and edits a <see cref="MealCardViewModel"/> and hosts the
/// DONE/PENDING actions, the photographs and the copying commands.
/// All cards share the same structure and the same height; the card is sized (MealCardMinHeight)
/// so the inline editor fits inside it with the photographs row - switching between reading and
/// editing never changes the card height, and every editor field gets real room.
/// </summary>
public sealed partial class MealCardControl : UserControl
{
    /// <summary>Identifies the <see cref="Card"/> dependency property.</summary>
    public static readonly DependencyProperty CardProperty = DependencyProperty.Register(
        nameof(Card),
        typeof(MealCardViewModel),
        typeof(MealCardControl),
        new PropertyMetadata(null));

    public MealCardControl() => InitializeComponent();

    /// <summary>The card this control renders.</summary>
    public MealCardViewModel? Card
    {
        get => (MealCardViewModel?)GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }

    private void OnOpenPhoto(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MealPhotoViewModel photo })
        {
            _ = Card?.OpenPhotoCommand.ExecuteAsync(photo);
        }
    }

    /// <summary>Removes the photograph whose X was tapped; the host asks for confirmation.</summary>
    private void OnDeletePhoto(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MealPhotoViewModel photo })
        {
            _ = Card?.RemovePhotoCommand.ExecuteAsync(photo);
        }
    }

    /// <summary>Clears the read-mode error when the user dismisses its InfoBar.</summary>
    private void OnReadInfoBarClosing(InfoBar sender, InfoBarClosingEventArgs args)
    {
        if (Card is not null)
        {
            Card.ErrorMessage = null;
        }
    }
}
