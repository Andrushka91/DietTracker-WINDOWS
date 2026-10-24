using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DietTracker.App.ViewModels;

/// <summary>
/// One meal state inside one statistics period: how many meal slots of the period are in that state
/// and which share of all its slots that is, so the view can paint a proportional bar and print the
/// percentage next to it.
/// </summary>
public sealed partial class StateSliceViewModel : ObservableObject
{
    /// <summary>
    /// Height of the indicator column. The page raises it together with the trend lanes so the
    /// statistics fill the whole panel height like the other screens.
    /// </summary>
    [ObservableProperty]
    private double _laneHeight = 140d;
    public StateSliceViewModel(string label, Brush fill, int count, int totalSlots)
    {
        Label = label;
        Fill = fill;
        Count = count;
        Percent = totalSlots <= 0 ? 0d : Math.Round(count * 100d / totalSlots, 1);
        BarFill = new GridLength(Percent, GridUnitType.Star);
        BarRest = new GridLength(Math.Max(0d, 100d - Percent), GridUnitType.Star);
    }

    /// <summary>Localized name of the state ("Done", "Missed", "Pending").</summary>
    public string Label { get; }

    /// <summary>Colour of the state - the same one the cards, the squares and the calendar use.</summary>
    public Brush Fill { get; }

    /// <summary>Number of meal slots of the period in this state.</summary>
    public int Count { get; }

    /// <summary>Share of the period's slots in this state, 0..100.</summary>
    public double Percent { get; }

    /// <summary>Percentage rendered as text under the bar.</summary>
    public string PercentText => FormattableString.Invariant($"{Percent:0.#} %");

    /// <summary>Slot count and percentage together ("12 · 40 %"), printed right of the bar.</summary>
    public string DetailText => FormattableString.Invariant($"{Count} · {Percent:0.#} %");

    /// <summary>Height of the filled part of the vertical bar (bottom rows, star share of <see cref="Percent"/>).</summary>
    public GridLength BarFill { get; }

    /// <summary>Height of the empty remainder of the vertical bar (top rows).</summary>
    public GridLength BarRest { get; }
}

/// <summary>
/// A whole statistics period (day, week or month) split into the three meal states: the day counts
/// and one vertical percentage indicator per state.
/// </summary>
public sealed class PeriodStateViewModel
{
    public required string Summary { get; init; }

    public required StateSliceViewModel Done { get; init; }

    public required StateSliceViewModel Missed { get; init; }

    public required StateSliceViewModel Pending { get; init; }

    /// <summary>The three states in a fixed order, so the view can render one indicator each.</summary>
    public IReadOnlyList<StateSliceViewModel> Slices => [Done, Missed, Pending];
}
