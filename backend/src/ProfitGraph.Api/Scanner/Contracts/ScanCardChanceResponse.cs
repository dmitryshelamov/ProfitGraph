namespace ProfitGraph.Api.Scanner.Contracts;

/// <summary>
///     A card's odds from the backtests' cards like it (ADR 0071, section 1; ADR 0073): the chance its target comes before its stop and
///     its cautious end (the 10th percentile), the share of such cards whose entry filled, the R expected of a trade that misses the
///     target, the result expected after costs in R with the chance and with its cautious end, and how many closed trades of the card's
///     bands the chance rests on. Decimals are exact strings rounded to four places.
/// </summary>
public sealed record ScanCardChanceResponse(
    string Chance,
    string ChanceLow,
    string FillShare,
    string MissedR,
    string ExpectedR,
    string ExpectedRLow,
    int Trades);