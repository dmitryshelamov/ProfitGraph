using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.Contracts;

/// <summary>
///     A backtest's closed card trades of one kind in one third of its period that pass an auto trade rule's minimums (ADR 0071,
///     section 5): how many and their mean R.
/// </summary>
public sealed record ScanCardGroupInternal(string Timeframe, ScanScenario Scenario, PositionDirection Direction, int Third, int Trades, decimal MeanR);