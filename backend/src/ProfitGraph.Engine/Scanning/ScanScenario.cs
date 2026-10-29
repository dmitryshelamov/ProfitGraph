namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     A trade the preset backtest simulates after each signal (ADR 0059, section 8).
/// </summary>
public enum ScanScenario
{
    /// <summary>
    ///     Entry at the next minute's open, stop at the nearest support, target at the nearest resistance.
    /// </summary>
    Levels,

    /// <summary>
    ///     <see cref="Levels" /> only when the entry's reward-to-risk is at least 2.
    /// </summary>
    LevelsRewardRisk2,

    /// <summary>
    ///     Entry at the next minute's open, stop at the nearest support, target two risks above the entry.
    /// </summary>
    Target2R,

    /// <summary>
    ///     A limit at half of the last hour's rise, valid for 4 hours; stop at the hour's lowest low, target two risks above the entry.
    /// </summary>
    Pullback
}