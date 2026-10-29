namespace ProfitGraph.Engine.Grids;

/// <summary>
///     The resolved settings of one grid block (ADR 0058, section 1); read at every evaluation, so a changed parameter moves
///     the grid's unfilled orders while bought levels keep their lots.
/// </summary>
public sealed record GridSettings
{
    public const int MaxLevels = 200;

    public int Levels { get; }
    public decimal Step { get; }
    public decimal ProfitStep { get; }
    public decimal Lot { get; }
    public int MaxOpenBuys { get; }
    public decimal ShiftThreshold { get; }
    public decimal AnchorCeiling { get; }
    public decimal StopBelow { get; }

    private GridSettings(
        int levels,
        decimal step,
        decimal profitStep,
        decimal lot,
        int maxOpenBuys,
        decimal shiftThreshold,
        decimal anchorCeiling,
        decimal stopBelow)
    {
        Levels = levels;
        Step = step;
        ProfitStep = profitStep;
        Lot = lot;
        MaxOpenBuys = maxOpenBuys;
        ShiftThreshold = shiftThreshold;
        AnchorCeiling = anchorCeiling;
        StopBelow = stopBelow;
    }

    /// <summary>
    ///     The settings, or null when a value is outside its range: 1–200 whole levels, a step and a stop in (0, 1) and [0, 1), a
    ///     positive profit step, lot and ceiling, at least one whole open buy and a nonnegative shift threshold.
    /// </summary>
    public static GridSettings? Create(
        decimal levels,
        decimal step,
        decimal profitStep,
        decimal lot,
        decimal maxOpenBuys,
        decimal shiftThreshold,
        decimal anchorCeiling,
        decimal stopBelow)
    {
        if (levels is < 1m or > MaxLevels || decimal.Truncate(levels) != levels || step is <= 0m or >= 1m || profitStep <= 0m || lot <= 0m ||
            maxOpenBuys < 1m || decimal.Truncate(maxOpenBuys) != maxOpenBuys || maxOpenBuys > MaxLevels || shiftThreshold < 0m ||
            anchorCeiling <= 0m || stopBelow is < 0m or >= 1m)
        {
            return null;
        }

        return new GridSettings((int)levels, step, profitStep, lot, (int)maxOpenBuys, shiftThreshold, anchorCeiling, stopBelow);
    }

    /// <summary>
    ///     The price of level <paramref name="index" />, counted from 1: anchor × (1 − step)^index.
    /// </summary>
    public decimal LevelPrice(decimal anchor, int index)
    {
        decimal price = anchor;

        for (var level = 0; level < index; level++)
        {
            price *= 1m - Step;
        }

        return price;
    }
}