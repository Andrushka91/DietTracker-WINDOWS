namespace DietTracker.Core.Utilities;

/// <summary>
/// Decides whether wrapped text still fits the area that shows it and how much of it has to be given up
/// when it does not. The calculation carries no UI framework type, so the rule behind the description
/// field - "the text may never be taller than the box" - can be unit tested on its own.
/// </summary>
public static class TextFit
{
    /// <summary>
    /// Slack, in device independent pixels, by which a wrapped extent may exceed the viewport without
    /// being called an overflow. The extent of a text block is the sum of per-line heights that are
    /// rounded on their own, so text that visibly fits can still report a fraction of a pixel too much
    /// - and a limit that reacts to that would refuse a character the user can plainly see.
    /// </summary>
    public const double OverflowTolerance = 0.5;

    /// <summary>
    /// Largest length difference still treated as one keystroke (or one backspace) rather than as a
    /// paste. A keystroke is answered by bringing back the previous text; a paste is cut instead, so
    /// whatever of it does fit is kept.
    /// </summary>
    public const int SmallEditDelta = 2;

    /// <summary>True when the wrapped content is taller than the viewport that shows it.</summary>
    public static bool Overflows(double extentHeight, double viewportHeight)
        => (extentHeight - viewportHeight) > OverflowTolerance;

    /// <summary>True when the change from <paramref name="accepted"/> to <paramref name="candidate"/> is small enough to be one keystroke.</summary>
    public static bool IsSmallEdit(string accepted, string candidate)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        ArgumentNullException.ThrowIfNull(candidate);

        return Math.Abs(candidate.Length - accepted.Length) <= SmallEditDelta;
    }

    /// <summary>
    /// Number of characters a wrapped field can accept, estimated from the text it already holds: the
    /// complete rows above the last one set the density (characters per row), and every row the box can
    /// still show is filled at that density. This is the wrapped counterpart of the title rule, where the
    /// room left on the single line is divided by the average character width.
    /// </summary>
    /// <param name="length">Characters currently in the box.</param>
    /// <param name="usedRows">Rows the current text occupies, at least 1.</param>
    /// <param name="firstCharacterOfLastRow">Index of the first character of the last occupied row.</param>
    /// <param name="visibleRows">Rows the box can show at its current size.</param>
    /// <param name="maxLength">The validator's ceiling, which no measurement may exceed.</param>
    /// <remarks>
    /// The estimate is deliberately an estimate: word wrapping leaves some slack at the end of every
    /// row, and a stored text can have short explicit lines, so the number returned can be a little
    /// above or below what the next typed character really fits. The exact fit rule of the field stays
    /// behind it, exactly as the title keeps its own exact rule behind the average-width cap: the cap
    /// stops the caret from wandering off the last visible row, the rule catches the character that
    /// still gets through. The result is never below the text already in the box, so a stored value is
    /// never swallowed by a cap that was lowered under it.
    /// </remarks>
    public static int WrappedCapacity(
        int length,
        int usedRows,
        int firstCharacterOfLastRow,
        int visibleRows,
        int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(usedRows);
        ArgumentOutOfRangeException.ThrowIfNegative(firstCharacterOfLastRow);
        ArgumentOutOfRangeException.ThrowIfNegative(visibleRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);

        // A single row has no complete row to take a density from; the box is far from full anyway and
        // the exact rule keeps it honest, so the cap stays where it is.
        if (length == 0 || usedRows <= 1 || visibleRows <= 1)
        {
            return Math.Min(length, maxLength);
        }

        var fullRows = usedRows - 1;
        if (firstCharacterOfLastRow <= 0 || visibleRows <= fullRows)
        {
            // No complete row above the last one, or the box cannot show the rows it already holds:
            // nothing may be added.
            return Math.Min(length, maxLength);
        }

        var charactersPerRow = firstCharacterOfLastRow / fullRows;
        if (charactersPerRow <= 0)
        {
            return Math.Min(length, maxLength);
        }

        var capacity = firstCharacterOfLastRow + ((visibleRows - fullRows) * charactersPerRow);
        return Math.Min(maxLength, Math.Max(length, capacity));
    }

    /// <summary>
    /// Length of the longest leading part of a <paramref name="candidateLength"/> characters long text
    /// that <paramref name="fits"/> accepts.
    /// </summary>
    /// <param name="candidateLength">Length of the whole text that is being tried.</param>
    /// <param name="fits">Called with a length; must answer whether the first that many characters fit.</param>
    /// <remarks>
    /// <para>
    /// <paramref name="fits"/> must be monotonic: when a text fits, every shorter leading part of it fits
    /// too. That holds for wrapped text - taking characters away from the end can only shorten the last
    /// line, never add one - which is what makes the binary search below correct.
    /// </para>
    /// <para>
    /// The search costs at most log2(candidateLength) calls, so pasting a long text is cut in a handful
    /// of measurements instead of one per character. The empty text is the implicit lower bound: it
    /// always fits, so the search never has to probe it.
    /// </para>
    /// </remarks>
    public static int LongestFittingLength(int candidateLength, Func<int, bool> fits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(candidateLength);
        ArgumentNullException.ThrowIfNull(fits);

        if (candidateLength == 0 || fits(candidateLength))
        {
            return candidateLength;
        }

        // Invariant: everything up to low fits (0 does, by definition), everything from high on does not.
        var low = 0;
        var high = candidateLength;
        while (high - low > 1)
        {
            var middle = low + ((high - low) / 2);
            if (fits(middle))
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
