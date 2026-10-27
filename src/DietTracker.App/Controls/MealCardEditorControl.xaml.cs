using System.Text;
using DietTracker.App.ViewModels;
using DietTracker.Core.Utilities;
using DietTracker.Core.Validation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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

    /// <summary>Guards the corrections the title fit check applies, so they cannot trigger themselves.</summary>
    private bool _titleFitting;

    /// <summary>The text currently accepted inside the title box.</summary>
    private string _title = string.Empty;

    /// <summary>Whether the title box currently holds the input.</summary>
    private bool _titleFocused;

    /// <summary>Whether <see cref="_title"/> is showing in full, without the box having to scroll to the right.</summary>
    private bool _titleFits = true;

    /// <summary>
    /// The horizontal offset the title box was scrolled to while <see cref="_title"/> was showing. A
    /// refused change has to take it back, exactly like <see cref="_descriptionView"/> does for the
    /// description: the TextBox brings its caret into view with an animation that keeps running towards
    /// a position the reverted text never had.
    /// </summary>
    private double _titleView;

    /// <summary>Guards the reformatting pass so it cannot trigger itself.</summary>
    private bool _reformatting;

    /// <summary>Guards the corrections the description fit check applies, so they cannot trigger themselves.</summary>
    private bool _fitting;

    /// <summary>The text currently accepted inside the description box.</summary>
    private string _description = string.Empty;

    /// <summary>Whether the description box currently holds the input (focus arrives on its inner content and bubbles here).</summary>
    private bool _descriptionFocused;

    /// <summary>Whether <see cref="_description"/> still fits the visible area of the box.</summary>
    private bool _descriptionFits = true;

    /// <summary>
    /// The offset the description box was scrolled to while <see cref="_description"/> was showing. A
    /// change that is refused has to take its scroll back as well: the TextBox brings the caret into
    /// view with an animation, and that animation keeps running towards a position the reverted text
    /// never had - visible as one line flickering up and back.
    /// </summary>
    private double _descriptionView;

    /// <summary>
    /// The height the description row actually gives the box while <see cref="_description"/> shows. The
    /// reference the next change is measured against; refreshed whenever the layout (not the text)
    /// changes the space - see <see cref="OnDescriptionSizeChanged"/>.
    /// </summary>
    private double _descriptionHeight;

    /// <summary>True while the fit check forces a layout pass, so its own side effects are not mistaken for a layout change.</summary>
    private bool _probing;

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

    /// <summary>
    /// Re-judges the stored title the moment the user starts editing it, exactly as the description does.
    /// The editor becomes visible together with the card, so the first pass of the binding often lands in
    /// a box that has no layout to measure yet; by the time focus arrives that layout exists. The answer
    /// decides whether a later overflow may be reverted (the text fitted before the edit) or must be left
    /// alone (it did not).
    /// </summary>
    private void OnTitleGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        _titleFocused = true;
        _title = box.Text;
        _titleFits = TitleFits(box) ?? true;
        _titleView = TitleView(box);
        ApplyTitleCapacity(box);
    }

    /// <summary>
    /// The room the title has is the card's width, so every width change is a change of the cap: the count
    /// the box may accept is recalculated from the new viewport. The event also fires the first time the
    /// editor is laid out, which is where the box gets its very own number instead of a written one.
    /// </summary>
    private void OnTitleSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // A change the fit check itself caused carries a half-written text (a paste is cut by trying
        // prefixes in the box), and that text must not set the cap.
        if (_titleFitting || sender is not TextBox box)
        {
            return;
        }

        ApplyTitleCapacity(box);
    }

    /// <summary>Stops the fit enforcement: nothing that leaves the box on its own may be edited away.</summary>
    private void OnTitleLostFocus(object sender, RoutedEventArgs e) => _titleFocused = false;

    /// <summary>
    /// Keeps the title inside the width the box gives its text: the instant the line would have to be
    /// scrolled to be finished - which is the same moment it would run under the clear (X) button - the
    /// change is refused: a keystroke brings back the last text that fitted, a paste is cut to the longest
    /// leading part of it that still fits. The field therefore never slides its text out of sight while
    /// somebody is typing in it, at any window width, and the number of characters it accepts is decided
    /// by the room in the card rather than by a fixed cap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ApplyTitleCapacity"/> hands the box the count it measured as its own MaxLength, so the
    /// caret normally stops at the edge before a character is typed at all; what happens here is the exact
    /// backstop for the change that cap could not foresee - a paste, or a character wider than the ones
    /// the average was taken from.
    /// </para>
    /// <para>
    /// A title that arrived from the database already too wide for the box is never cut: the user must be
    /// able to open, read and save it. Such a text simply cannot grow until enough of it has been deleted
    /// to come back inside the box, which is also why the state of the last accepted text is tracked.
    /// </para>
    /// </remarks>
    private void OnTitleTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_titleFitting || sender is not TextBox box)
        {
            return;
        }

        var candidate = box.Text;
        if (candidate == _title)
        {
            return;
        }

        var fits = TitleFits(box);
        if (fits is null || fits.Value)
        {
            // The whole line is showing, or the box has no layout to judge it by (the editor is still
            // collapsed, the card has never been measured): accept it and wait for a focusable moment.
            _title = candidate;
            _titleFits = fits ?? _titleFits;
            if (fits == true)
            {
                _titleView = TitleView(box);
            }

            ApplyTitleCapacity(box);
            return;
        }

        if (!_titleFocused)
        {
            // The binding pushed a stored value that is too wide for the box. Keep it untouched, but cap
            // the box at it: nothing may be added to a title the card cannot show in full.
            _title = candidate;
            _titleFits = false;
            ApplyTitleCapacity(box);
            return;
        }

        if (candidate.Length < _title.Length)
        {
            // The text became shorter and still does not fit. Accept it anyway: shortening is the only way
            // a stored title too wide for the card comes back inside it, so a change that helps must never
            // be refused - it would lock the field on every backspace. Recalculating the cap here is what
            // hands the freed room back to the user one character at a time.
            _title = candidate;
            _titleView = TitleView(box);
            ApplyTitleCapacity(box);
            return;
        }

        var caret = Math.Min(box.SelectionStart, candidate.Length);
        _titleFitting = true;
        try
        {
            if (!_titleFits || TextFit.IsSmallEdit(_title, candidate))
            {
                // Either the text was already too wide before this edit - nothing may be added to it, and
                // cutting it back would destroy stored words the user never touched - or this is one
                // keystroke over the end of the line. Both are answered by bringing the previous text
                // back, which is exactly what fitted.
                box.Text = _title;
            }
            else
            {
                // A longer paste: keep as much of it as the box can still show.
                var kept = candidate[..TextFit.LongestFittingLength(candidate.Length, size => TitleShows(box, candidate[..size]))];
                box.Text = kept;
                _title = kept;
            }

            box.SelectionStart = Math.Min(caret, box.Text.Length);

            // Judge the text that is back in the box against the space it has now: a card narrowed since
            // the text was accepted reports even that text as too wide, and the next keystroke is simply
            // refused the same way until enough of it has been deleted to come back inside the box.
            _titleFits = TitleFits(box) ?? _titleFits;

            // The cap goes back to the text that is really in the box now, so the next keystroke is
            // stopped by the box itself instead of by this handler.
            ApplyTitleCapacity(box);

            // The refused change already made the TextBox start its animated scroll towards the caret.
            // Bring the view back to the position the accepted text was shown at, without animation, so
            // the refusal is invisible instead of sliding the line to the right and back. Done last: the
            // layout pass above is where that scroll is issued.
            RestoreTitleView(box, _titleView);
        }
        finally
        {
            _titleFitting = false;
        }
    }

    /// <summary>
    /// Puts one candidate text into the title box and reports whether the box can show all of it. A box
    /// with no layout answers "yes", so a measurement that cannot be taken never cuts the user's text.
    /// </summary>
    private bool TitleShows(TextBox box, string text)
    {
        box.Text = text;
        return TitleFits(box) ?? true;
    }

    /// <summary>
    /// Whether the title box shows the whole line: the width the text asks for is measured against the
    /// viewport of the scroll viewer the TextBox carries inside itself. That viewport is one column of the
    /// template and the clear (X) button is the next one (30 px wide, shown as soon as the box has any
    /// text), so the button already took its room away from the text - "nothing has to be scrolled" is
    /// precisely "the line ends before the X button".
    /// </summary>
    /// <remarks>
    /// Unlike the description, the box never grows with its text here: a single line only asks for the
    /// width it is offered, so the viewport read at every change is the whole reference the rule needs and
    /// a card that became narrower heals itself instead of locking the field.
    /// </remarks>
    /// <returns>Null when there is nothing to measure yet (the editor is collapsed or not loaded).</returns>
    private bool? TitleFits(TextBox box)
        => TitleScroller(box) is ScrollViewer scroller
            ? !TextFit.Overflows(scroller.ExtentWidth, scroller.ViewportWidth)
            : null;

    /// <summary>
    /// The scroll viewer a title box shows its line through, once the box has been laid out around the
    /// text it holds right now - the measurements of both the fit rule and the cap are taken from it.
    /// </summary>
    /// <returns>Null when there is nothing to measure yet (the editor is collapsed or not loaded).</returns>
    private ScrollViewer? TitleScroller(TextBox box)
    {
        if (FindContentScroller(box) is not ScrollViewer scroller)
        {
            return null;
        }

        ProbeLayout(box);

        return scroller.ViewportWidth > 0 ? scroller : null;
    }

    /// <summary>
    /// Runs the layout pass a change to a text box triggers, so the measurements taken right after it
    /// describe the text that is in the box now. The pass is marked as our own probing so the description
    /// does not mistake it for its space changing.
    /// </summary>
    private void ProbeLayout(TextBox box)
    {
        _probing = true;
        try
        {
            box.UpdateLayout();
        }
        finally
        {
            _probing = false;
        }
    }

    /// <summary>
    /// Writes the character cap the title box can actually show into the box itself: the room its line has
    /// left before it reaches the clear (X) button, divided by the average width of the characters already
    /// typed. That is what makes the limit dynamic instead of a number written in the XAML - it is
    /// recalculated after every change and on every width of the card - and it lets the TextBox stop the
    /// caret at the edge by itself, so no character is ever typed and taken back.
    /// </summary>
    /// <remarks>
    /// The characters already in the box stand for the ones the user will type next; the measurement rule
    /// of <see cref="OnTitleTextChanged"/> stays behind the cap as the exact answer for the case where the
    /// next character is wider than that average (a wide "W" after a row of narrow "i"-s). The cap is
    /// never put below the text the box holds, so lowering it cannot swallow a stored title, and an empty
    /// box opens up to the validator's cap - there is no typed character to take a width from yet.
    /// </remarks>
    private void ApplyTitleCapacity(TextBox box)
    {
        var length = box.Text.Length;
        if (length == 0)
        {
            box.MaxLength = MealEntryValidator.MaxTitleLength;
            return;
        }

        ProbeLayout(box);

        if (TitleCapacity(box) is not (var room, var average))
        {
            return;
        }

        var capacity = room > 0 && average > 0
            ? length + (int)Math.Floor(room / average)
            : length;

        box.MaxLength = Math.Min(MealEntryValidator.MaxTitleLength, Math.Max(length, capacity));
    }

    /// <summary>
    /// The room a title line still has and the average width of the characters in it, both read where the
    /// text is actually drawn: the rectangles the TextBox reports for the caret at the start and at the end
    /// of its line, and the left edge of the clear (X) button, which is where the line has to stop. The
    /// scroll viewer cannot answer this - it clamps the extent of a line that fits to its own viewport, so
    /// a box with room to spare keeps reporting that room as taken.
    /// </summary>
    /// <returns>Null when there is nothing to measure (an empty line, or a box with no layout yet).</returns>
    private static (double Room, double Average)? TitleCapacity(TextBox box)
    {
        var length = box.Text.Length;
        if (length == 0)
        {
            return null;
        }

        // Both rectangles carry the same inset of the box, so their difference is the width of the line
        // itself - and it stays correct while the box is scrolled, where both ends shift together. The
        // first character is measured from its leading edge, the last one to its trailing edge, so the
        // difference spans the whole line; the caret rectangle at Text.Length is not available here,
        // because GetRectFromCharacterIndex takes the index of a character, not of a gap.
        var start = box.GetRectFromCharacterIndex(0, trailingEdge: false).Left;
        var end = box.GetRectFromCharacterIndex(length - 1, trailingEdge: true).Left;
        var used = end - start;
        if (used <= 0)
        {
            return null;
        }

        return (Math.Max(0d, TitleStopLine(box) - end), used / length);
    }

    /// <summary>
    /// The x a title line must not pass: the left edge of the clear (X) button the TextBox shows as soon as
    /// it has any text, in the coordinate space of the box. The button is a template part named
    /// "DeleteButton"; where it cannot be found (a renamed part, a collapsed button) the right inner edge
    /// of the box stands in for it.
    /// </summary>
    private static double TitleStopLine(TextBox box)
    {
        if (FindDeleteButton(box) is Button button && button.ActualWidth > 0)
        {
            return button.TransformToVisual(box).TransformPoint(new Windows.Foundation.Point(0, 0)).X;
        }

        return box.ActualWidth - box.BorderThickness.Right - box.Padding.Right;
    }

    /// <summary>
    /// The clear (X) button of a text box, by its template part name; the visual tree is searched as a
    /// fallback, for the case where the name scope is not reachable from outside the control. The only
    /// button a TextBox template carries is that one.
    /// </summary>
    private static Button? FindDeleteButton(TextBox box)
    {
        if (box.FindName("DeleteButton") is Button named)
        {
            return named;
        }

        var pending = new Stack<DependencyObject>();
        pending.Push(box);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
            {
                var child = VisualTreeHelper.GetChild(current, index);
                if (child is Button button)
                {
                    return button;
                }

                pending.Push(child);
            }
        }

        return null;
    }

    /// <summary>
    /// The horizontal offset a title box is scrolled to right now; 0 while it has nothing to measure. The
    /// accepted text is what has to be on screen again once a change has been refused.
    /// </summary>
    private static double TitleView(TextBox box)
        => FindContentScroller(box) is ScrollViewer scroller ? scroller.HorizontalOffset : 0d;

    /// <summary>
    /// Scrolls a title box back to an offset, skipping the animation the TextBox itself would use. An
    /// offset past the end of a now shorter line is clamped by the scroll viewer, so it is safe to hand it
    /// the position as it was recorded.
    /// </summary>
    private static void RestoreTitleView(TextBox box, double offset)
    {
        void Pin()
        {
            if (FindContentScroller(box) is ScrollViewer scroller)
            {
                scroller.ChangeView(null, offset, null, disableAnimation: true);
            }
        }

        Pin();

        // The caret scroll of the TextBox is not always issued inside this handler - it can also come one
        // pass later, when it brings the caret into view. Pinning again on the next turn of the queue
        // catches that one too; no key can be processed in between, so nothing visible happens twice.
        box.DispatcherQueue?.TryEnqueue(Pin);
    }

    /// <summary>
    /// Re-judges the stored text the moment the user starts editing it. The editor becomes visible
    /// together with the card, so the first pass of the binding often lands in a box that has no layout
    /// to measure yet; by the time focus arrives that layout exists. The answer decides whether a later
    /// overflow may be cut back (the text fitted before the edit) or must be left alone (it did not).
    /// </summary>
    private void OnDescriptionGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        _descriptionFocused = true;
        _description = box.Text;
        _descriptionFits = DescriptionFits(box) ?? true;
        _descriptionView = DescriptionView(box);
        _descriptionHeight = box.ActualHeight;
        ApplyDescriptionCapacity(box);
    }

    /// <summary>
    /// A height change the fit check did not cause is the layout itself changing: an error InfoBar
    /// appearing or clearing, the window being resized, the card being re-laid out. The box is then only
    /// taking the space it is given, and that space becomes the new reference - without this the box
    /// growing back after an InfoBar clears would look like an overflowing character forever after.
    /// </summary>
    private void OnDescriptionSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_probing || _fitting || sender is not TextBox box)
        {
            return;
        }

        _descriptionHeight = box.ActualHeight;
        _descriptionView = DescriptionView(box);
        ApplyDescriptionCapacity(box);
    }

    /// <summary>Stops the fit enforcement: nothing that leaves the box on its own may be edited away.</summary>
    private void OnDescriptionLostFocus(object sender, RoutedEventArgs e) => _descriptionFocused = false;

    /// <summary>
    /// Keeps the description inside the room the box shows: the instant the wrapped text would need the
    /// vertical scrollbar - or would make the box taller than the space its row gives it - the change is
    /// refused: a keystroke brings back the last text that fitted, a paste is cut to the longest leading
    /// part of it that still fits. The field therefore never scrolls and never stretches the card while
    /// somebody is typing in it, at any window width, and the number of characters it accepts is decided
    /// by the space in the card rather than by a fixed cap.
    /// </summary>
    /// <remarks>
    /// A value that arrived from the database already too long to fit is never cut: the user must be able
    /// to open, read and save it. Such a text simply cannot grow until enough of it has been deleted to
    /// come back inside the box, which is also why the state of the last accepted text is tracked.
    /// </remarks>
    private void OnDescriptionTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_fitting || sender is not TextBox box)
        {
            return;
        }

        var candidate = box.Text;
        if (candidate == _description)
        {
            return;
        }

        var fits = DescriptionFits(box);
        if (fits is null || fits.Value)
        {
            // The text is inside the box, or the box has no layout to judge it by (the editor is still
            // collapsed, the card has never been measured): accept it and wait for a focusable moment.
            _description = candidate;
            _descriptionFits = fits ?? _descriptionFits;
            if (fits == true)
            {
                _descriptionView = DescriptionView(box);
                _descriptionHeight = box.ActualHeight;
            }

            ApplyDescriptionCapacity(box);
            return;
        }

        if (!_descriptionFocused)
        {
            // The binding pushed a stored value that is too tall for the box. Keep it untouched, but cap
            // the box at it: nothing may be added to a description the card cannot show in full.
            _description = candidate;
            _descriptionFits = false;
            ApplyDescriptionCapacity(box);
            return;
        }

        var caret = Math.Min(box.SelectionStart, candidate.Length);
        _fitting = true;
        try
        {
            if (!_descriptionFits)
            {
                // The text was already too tall before this edit: nothing may be added to it, and cutting
                // it back would destroy stored words the user never touched.
                box.Text = _description;
            }
            else if (TextFit.IsSmallEdit(_description, candidate))
            {
                // A character typed (or a word pasted) over the last line: the previous text is exactly
                // what has to come back.
                box.Text = _description;
            }
            else
            {
                // A longer paste: keep as much of it as the box can still show.
                var kept = candidate[..TextFit.LongestFittingLength(candidate.Length, size => Shows(box, candidate[..size]))];
                box.Text = kept;
                _description = kept;
            }

            box.SelectionStart = Math.Min(caret, box.Text.Length);

            // Judge the text that is back in the box against the reference it was accepted with...
            _descriptionFits = DescriptionFits(box) ?? _descriptionFits;

            // ...and then let whatever space the box has now become that reference. The reverted text is
            // by definition the accepted one, so its height is the truth for the next change; this also
            // heals a growth the refused change did not cause (another card changing height, an InfoBar
            // appearing or clearing), which would otherwise read as an overflow on every keystroke after
            // it and lock the field.
            _descriptionHeight = box.ActualHeight;

            // Hand the freed/gained room to the box itself, so the next keystroke is stopped by the cap
            // instead of being typed and taken back.
            ApplyDescriptionCapacity(box);

            // The refused change already made the TextBox start its animated scroll towards the caret.
            // Bring the view back to the position the accepted text was shown at, without animation, so
            // the refusal is invisible instead of flickering one line up and down. Done last: the layout
            // pass above is where that scroll is issued.
            RestoreDescriptionView(box, _descriptionView);
        }
        finally
        {
            _fitting = false;
        }
    }

    /// <summary>
    /// Puts one candidate text into the box and reports whether the box can show all of it. A box with
    /// no layout answers "yes", so a measurement that cannot be taken never cuts the user's text.
    /// </summary>
    private bool Shows(TextBox box, string text)
    {
        box.Text = text;
        return DescriptionFits(box) ?? true;
    }

    /// <summary>
    /// Writes the character cap the description box can actually show into the box itself, the wrapped
    /// counterpart of <see cref="ApplyTitleCapacity"/>: the rows the box can show are filled at the
    /// character density of the complete rows the text already has, and the result becomes the box's own
    /// MaxLength. The caret therefore stops when the last visible row is used up, instead of typing one
    /// character too many and waiting for the exact fit rule of <see cref="OnDescriptionTextChanged"/>
    /// to take it back. Recalculated after every change and on every size change, so a wider card
    /// accepts more and a narrower one fewer.
    /// </summary>
    /// <remarks>
    /// The exact fit rule stays behind this cap: the density of the rows already typed is only an average,
    /// so a paste, a very wide character or an unusual word wrap can still ask for more room than the cap
    /// predicted - and that is the case the rule answers. A text that already needs a scrollbar (a stored
    /// value longer than the box, or one the card was narrowed under) is capped at itself: nothing may be
    /// added, but nothing is cut either, so it can still be opened, shortened and saved.
    /// </remarks>
    private void ApplyDescriptionCapacity(TextBox box)
    {
        var length = box.Text.Length;
        if (length == 0)
        {
            box.MaxLength = MealEntryValidator.MaxIngredientsLength;
            return;
        }

        ProbeLayout(box);

        if (FindContentScroller(box) is not ScrollViewer scroller
            || scroller.ViewportWidth <= 0d
            || scroller.ViewportHeight <= 0d)
        {
            return;
        }

        // No room left at all: the cap is the text already in the box. This is also what keeps a stored
        // description longer than the box from being cut: it simply cannot grow until it fits again.
        if (TextFit.Overflows(scroller.ExtentHeight, scroller.ViewportHeight))
        {
            box.MaxLength = Math.Min(MealEntryValidator.MaxIngredientsLength, length);
            return;
        }

        if (DescriptionGeometry(box) is not var (lineHeight, usedRows, firstCharacterOfLastRow)
            || usedRows < 2)
        {
            // A single row (or a box with no measurable glyphs) has no complete row above the last one to
            // take a density from. The box is far from full and the exact rule still guards it, so the
            // cap is left as it was rather than guessed at.
            return;
        }

        var visibleRows = (int)Math.Floor((scroller.ViewportHeight + TextFit.OverflowTolerance) / lineHeight);
        var capacity = TextFit.WrappedCapacity(
            length,
            usedRows,
            firstCharacterOfLastRow,
            visibleRows,
            MealEntryValidator.MaxIngredientsLength);

        box.MaxLength = Math.Min(MealEntryValidator.MaxIngredientsLength, Math.Max(length, capacity));
    }

    /// <summary>
    /// The rows the description's current text occupies and where its last row starts, read from the
    /// glyph rectangles the TextBox reports. The top of a character's rectangle is constant on one wrapped
    /// row and grows with every row, so the number of rows comes from the first and the last character and
    /// the first character of the last row is found by bisecting that monotonic sequence - a handful of
    /// measurements, no layout passes.
    /// </summary>
    /// <returns>Null when there is nothing to measure (an empty text, or a box with no layout yet).</returns>
    private static (double LineHeight, int UsedRows, int FirstCharacterOfLastRow)? DescriptionGeometry(TextBox box)
    {
        var length = box.Text.Length;
        if (length == 0)
        {
            return null;
        }

        // The rectangles are relative to the box and shift together when the box scrolls, so their
        // difference is the height of the text itself. The height of an edge rectangle is the line height.
        var first = box.GetRectFromCharacterIndex(0, trailingEdge: false);
        var last = box.GetRectFromCharacterIndex(length - 1, trailingEdge: true);
        if (first.Height <= 0d || last.Height <= 0d || last.Top < first.Top)
        {
            return null;
        }

        var lineHeight = first.Height;
        var usedRows = 1 + (int)Math.Floor(((last.Top - first.Top) + TextFit.OverflowTolerance) / lineHeight);
        if (usedRows <= 1)
        {
            return (lineHeight, 1, 0);
        }

        // First character of the last row: everything before it sits on an earlier row, everything from
        // it on sits on the last one. Half a line is the boundary that keeps rounding out of the test.
        var boundary = last.Top - (lineHeight / 2d);
        var low = 0;
        var high = length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            var rect = box.GetRectFromCharacterIndex(middle, trailingEdge: false);
            if (rect.Height > 0d && rect.Top >= boundary)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return (lineHeight, usedRows, low);
    }

    /// <summary>
    /// Whether the description box shows all of its text: the wrapped content is measured against the
    /// viewport of the scroll viewer the TextBox carries inside itself, and the height the box ended up
    /// with is measured against the space its row was giving it.
    /// </summary>
    /// <remarks>
    /// The second test is the one that catches an overflowing line as it happens. A star row hands its
    /// child at least what the child asks for, so a box that grows with its text first stretches the card
    /// (which only has a MinHeight) and grows its own viewport along with it - the content then never
    /// looks like it overflows, and the extension is on screen one line before anything refuses it.
    /// </remarks>
    /// <returns>Null when there is nothing to measure yet (the editor is collapsed or not loaded).</returns>
    private bool? DescriptionFits(TextBox box)
    {
        if (FindContentScroller(box) is not ScrollViewer scroller)
        {
            return null;
        }

        // The extent of the wrapped text is only known after the layout pass the change triggers, and
        // that pass is what makes the box ask for the extra line - so it is marked as our own probing.
        _probing = true;
        try
        {
            box.UpdateLayout();
        }
        finally
        {
            _probing = false;
        }

        if (scroller.ViewportHeight <= 0)
        {
            return null;
        }

        if (TextFit.Overflows(scroller.ExtentHeight, scroller.ViewportHeight))
        {
            return false;
        }

        // The box may not have become taller than the space its row was giving it. This is the test that
        // actually catches the first overflowing line: a star row hands the child at least what the child
        // asks for, so the box grows into the card's slack and the viewport grows with it - the overflow
        // above stays blind until one line later, which is the frame where the card visibly extended.
        return _descriptionHeight <= 0 || !TextFit.Overflows(box.ActualHeight, _descriptionHeight);
    }

    /// <summary>The offset a description box is scrolled to right now; 0 while it has nothing to measure.</summary>
    private static double DescriptionView(TextBox box)
        => FindContentScroller(box) is ScrollViewer scroller ? scroller.VerticalOffset : 0d;

    /// <summary>
    /// Scrolls a description box back to an offset, skipping the animation the TextBox itself would use.
    /// An offset past the end of a now shorter text is clamped by the scroll viewer, so it is safe to
    /// hand it the position as it was recorded.
    /// </summary>
    private static void RestoreDescriptionView(TextBox box, double offset)
    {
        void Pin()
        {
            if (FindContentScroller(box) is ScrollViewer scroller)
            {
                scroller.ChangeView(null, offset, null, disableAnimation: true);
            }
        }

        Pin();

        // The caret scroll of the TextBox is not always issued inside this handler - it can also come one
        // pass later, when it brings the caret into view. Pinning again on the next turn of the queue
        // catches that one too; no key can be processed in between, so nothing visible happens twice.
        box.DispatcherQueue?.TryEnqueue(Pin);
    }

    /// <summary>
    /// The scroll viewer a WinUI TextBox wraps its text in. It is a template part named
    /// "ContentElement"; the visual tree is searched only as a fallback, for the case where the part is
    /// renamed by a future toolkit version or the name scope is not reachable from outside the control.
    /// </summary>
    private static ScrollViewer? FindContentScroller(TextBox box)
    {
        if (box.FindName("ContentElement") is ScrollViewer named)
        {
            return named;
        }

        var pending = new Stack<DependencyObject>();
        pending.Push(box);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
            {
                var child = VisualTreeHelper.GetChild(current, index);
                if (child is ScrollViewer scroller)
                {
                    return scroller;
                }

                pending.Push(child);
            }
        }

        return null;
    }
}
