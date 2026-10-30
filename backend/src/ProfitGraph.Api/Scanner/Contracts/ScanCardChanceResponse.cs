namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A card's odds from the latest preset backtest's cards like it (ADR 0071, section 1): the chance its target comes before its stop,
///     the share of such cards whose entry filled, the mean R of those that missed the target, the result expected after costs in R and
///     how many closed trades the chance rests on. Decimals are exact strings rounded to four places.
/// </summary>
public sealed record ScanCardChanceResponse(string Chance, string FillShare, string MissedR, string ExpectedR, int Trades);