using System.ComponentModel;
using DietTracker.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DietTracker.App.Controls;

/// <summary>
/// The permanent meal card: reads and edits a <see cref="MealCardViewModel"/> and hosts the
/// DONE/PENDING actions, the photographs and the copying commands.
/// All cards share the same structure and the same height; nothing scrolls inside a card while
/// reading - and the inline editor is capped to the read height, so editing never grows it either.
/// </summary>
public sealed partial class MealCardControl : UserControl
{
    /// <summary>Identifies the <see cref="Card"/> dependency property.</summary>
    public static readonly DependencyProperty CardProperty = DependencyProperty.Register(
        nameof(Card),
        typeof(MealCardViewModel),
        typeof(MealCardControl),
        new PropertyMetadata(null, OnCardChanged));

    /// <summary>Height the read-mode content (row 1) occupies while the card is being read.</summary>
    private double _readContentHeight;

    /// <summary>Height of the read-mode action bar (row 3), which collapses while editing.</summary>
    private double _actionsBarHeight;

    public MealCardControl()
    {
        InitializeComponent();

        // Remember the heights of the two rows the inline editor replaces, measured while they are
        // visible (reading). SizeChanged also fires with 0 when a panel collapses, so only values
        // taken while visible are kept - this is what lets the editor reuse the exact space of the
        // read layout, so opening a card for editing never changes the card or window height.
        ReadPanel.SizeChanged += (_, _) =>
        {
            if (ReadPanel.Visibility == Visibility.Visible)
            {
                _readContentHeight = ReadPanel.ActualHeight;
            }
        };

        ActionsRow.SizeChanged += (_, _) =>
        {
            if (ActionsRow.Visibility == Visibility.Visible)
            {
                _actionsBarHeight = ActionsRow.ActualHeight;
            }
        };
    }

    /// <summary>The card this control renders.</summary>
    public MealCardViewModel? Card
    {
        get => (MealCardViewModel?)GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }

    private static void OnCardChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var cardControl = (MealCardControl)dependencyObject;

        if (args.OldValue is MealCardViewModel oldCard)
        {
            oldCard.PropertyChanged -= cardControl.OnCardPropertyChanged;
        }

        if (args.NewValue is MealCardViewModel newCard)
        {
            newCard.PropertyChanged += cardControl.OnCardPropertyChanged;
        }

        cardControl.ApplyEditorHeight();
    }

    private void OnCardPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MealCardViewModel.IsEditing))
        {
            ApplyEditorHeight();
        }
    }

    /// <summary>
    /// Pins the inline editor to the exact height the read content plus its action bar had, so
    /// switching between reading and editing never changes the card (and with it the window) height.
    /// The row gaps balance by construction: in read mode the photos row is sandwiched between two
    /// 8 px gaps and the action bar follows below it; in edit mode that action bar row collapses,
    /// but the editor carries its own action bar and exactly the former action-bar height is folded
    /// into the editor - the two layouts end up pixel-identical. The editor's roomy description
    /// field scrolls internally when its text is longer than the fixed space.
    /// </summary>
    private void ApplyEditorHeight()
    {
        if (Card is not { IsEditing: true })
        {
            EditorPanel.Height = double.NaN;
            return;
        }

        EditorPanel.Height = Math.Max(0, _readContentHeight + _actionsBarHeight);
    }

    private void OnOpenPhoto(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MealPhotoViewModel photo })
        {
            _ = Card?.OpenPhotoCommand.ExecuteAsync(photo);
        }
    }
}
