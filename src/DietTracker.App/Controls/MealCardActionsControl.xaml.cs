using DietTracker.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DietTracker.App.Controls;

/// <summary>Action bar shown under a meal card (DONE, undo, edit, photo, copy, template).</summary>
public sealed partial class MealCardActionsControl : UserControl
{
    /// <summary>Identifies the <see cref="Card"/> dependency property.</summary>
    public static readonly DependencyProperty CardProperty = DependencyProperty.Register(
        nameof(Card),
        typeof(MealCardViewModel),
        typeof(MealCardActionsControl),
        new PropertyMetadata(null));

    public MealCardActionsControl() => InitializeComponent();

    /// <summary>The card the actions apply to.</summary>
    public MealCardViewModel? Card
    {
        get => (MealCardViewModel?)GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }
}
