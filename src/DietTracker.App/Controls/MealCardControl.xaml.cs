using DietTracker.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DietTracker.App.Controls;

/// <summary>
/// The permanent meal card: reads and edits a <see cref="MealCardViewModel"/> and hosts the
/// DONE/PENDING actions, the photographs and the copying commands.
/// All cards share the same structure and the same floor height: <c>MealCardMinHeight</c> is sized
/// for the compact inline editor (title, short description, time, photo, Save/Cancel), so a card
/// without photographs keeps exactly the same height in both modes. A card that carries photographs
/// is taller while reading, because the photographs row collapses while the editor is open.
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

    /// <summary>Clears the read-mode error when the user dismisses its InfoBar.</summary>
    private void OnReadInfoBarClosing(InfoBar sender, InfoBarClosingEventArgs args)
    {
        if (Card is not null)
        {
            Card.ErrorMessage = null;
        }
    }
}
