using System.Text;
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

    /// <summary>Guards the reformatting pass so it cannot trigger itself.</summary>
    private bool _reformatting;

    /// <summary>
    /// Keeps the planned time input in "HH:MM" shape while accepting digits only: every non-digit the
    /// user types or pastes is discarded, at most four digits are kept and the ":" separator is
    /// inserted automatically after the first two digits.
    /// </summary>
    private void OnPlannedTimeTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_reformatting || sender is not TextBox box)
        {
            return;
        }

        var digits = new StringBuilder(4);
        foreach (var character in box.Text)
        {
            if (char.IsAsciiDigit(character))
            {
                digits.Append(character);
                if (digits.Length == 4)
                {
                    break;
                }
            }
        }

        var typed = digits.ToString();
        var formatted = typed.Length > 2
            ? $"{typed[..2]}:{typed[2..]}"
            : typed;

        if (formatted == box.Text)
        {
            return;
        }

        // Keep the caret after the same number of digits the user had typed up to it.
        var caret = Math.Min(box.SelectionStart, box.Text.Length);
        var digitsBeforeCaret = 0;
        for (var index = 0; index < caret; index++)
        {
            if (char.IsAsciiDigit(box.Text[index]))
            {
                digitsBeforeCaret++;
            }
        }

        _reformatting = true;
        try
        {
            box.Text = formatted;
            box.SelectionStart = PositionAfterDigits(formatted, digitsBeforeCaret);
        }
        finally
        {
            _reformatting = false;
        }
    }

    /// <summary>Index just behind the given number of digits in the formatted text.</summary>
    private static int PositionAfterDigits(string text, int digitCount)
    {
        var seen = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsAsciiDigit(text[index]))
            {
                seen++;
                if (seen == digitCount)
                {
                    return index + 1;
                }
            }
        }

        return text.Length;
    }
}
