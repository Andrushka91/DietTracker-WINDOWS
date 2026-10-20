using DietTracker.App.ViewModels;
using DietTracker.Core.Enums;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DietTracker.App.Controls;

/// <summary>Edits one ingredient line of a meal card (name, amount, unit).</summary>
public sealed partial class IngredientLineEditor : UserControl
{
    /// <summary>Identifies the <see cref="Line"/> dependency property.</summary>
    public static readonly DependencyProperty LineProperty = DependencyProperty.Register(
        nameof(Line),
        typeof(IngredientLineViewModel),
        typeof(IngredientLineEditor),
        new PropertyMetadata(null));

    public IngredientLineEditor() => InitializeComponent();

    /// <summary>Measurement units offered by the editor (bound by <c>x:Bind</c>, which cannot bind to a static member).</summary>
    public IReadOnlyList<MeasurementUnit> Units => IngredientLineViewModel.AvailableUnits;

    /// <summary>The ingredient line this editor is bound to.</summary>
    public IngredientLineViewModel? Line
    {
        get => (IngredientLineViewModel?)GetValue(LineProperty);
        set => SetValue(LineProperty, value);
    }

    private void OnRemove(object sender, RoutedEventArgs e) => Line?.Remove();
}
