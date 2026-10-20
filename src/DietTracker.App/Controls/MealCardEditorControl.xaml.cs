using DietTracker.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DietTracker.App.Controls;

/// <summary>
/// Inline editor of a meal card. It is hosted inside the card itself, so an everyday change never
/// requires navigating to another page.
/// </summary>
public sealed partial class MealCardEditorControl : UserControl
{
    /// <summary>Identifies the <see cref="Card"/> dependency property.</summary>
    public static readonly DependencyProperty CardProperty = DependencyProperty.Register(
        nameof(Card),
        typeof(MealCardViewModel),
        typeof(MealCardEditorControl),
        new PropertyMetadata(null));

    public MealCardEditorControl() => InitializeComponent();

    /// <summary>The card being edited.</summary>
    public MealCardViewModel? Card
    {
        get => (MealCardViewModel?)GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }
}
