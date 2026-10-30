using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Domain.Scanner.AutoTrades;

/// <summary>
///     Which card of a signal an auto trade rule takes (ADR 0071, section 4): its direction, scenario and levels' timeframe, and the
///     least expected R and chance of the card's odds (section 1) it accepts; no minimum chance accepts any.
/// </summary>
public sealed record ScanAutoTradeCard
{
    public PositionDirection Direction { get; }
    public ScanScenario Scenario { get; }
    public string Timeframe { get; }
    public decimal MinExpectedR { get; }
    public decimal? MinChance { get; }

    public ScanAutoTradeCard(PositionDirection direction, ScanScenario scenario, string timeframe, decimal minExpectedR, decimal? minChance)
    {
        if (!Enum.IsDefined(direction) || !Enum.IsDefined(scenario) || string.IsNullOrWhiteSpace(timeframe))
        {
            throw new ScanAutoTradeRuleException(ScanAutoTradeRuleException.InvalidRule, "An auto trade rule names a direction, a scenario and a timeframe.");
        }

        if (minChance is < 0m or > 1m)
        {
            throw new ScanAutoTradeRuleException(ScanAutoTradeRuleException.InvalidRule, "A minimum chance lies between 0 and 1.");
        }

        Direction = direction;
        Scenario = scenario;
        Timeframe = timeframe;
        MinExpectedR = minExpectedR;
        MinChance = minChance;
    }

    /// <summary>
    ///     Whether a card of these odds passes the minimums.
    /// </summary>
    public bool Accepts(decimal expectedR, decimal chance)
    {
        return expectedR >= MinExpectedR && (MinChance is not { } least || chance >= least);
    }
}