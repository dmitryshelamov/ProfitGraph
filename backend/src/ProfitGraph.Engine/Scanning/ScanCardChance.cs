namespace ProfitGraph.Engine.Scanning;

/// <summary>
///     A card's odds from the backtests' cards like it (ADR 0071, section 1; ADR 0073): the chance that its target comes before its stop
///     and its cautious end (the 10th percentile), the share of such cards whose entry filled, the R expected of a trade that misses
///     the target, the result expected after costs, <c>fill × (chance × the card's R at its target + (1 − chance) × the missed R)</c>,
///     and the same with the cautious chance, and how many closed trades of the card's bands the chance rests on.
/// </summary>
public sealed record ScanCardChance(
    decimal Chance,
    decimal ChanceLow,
    decimal FillShare,
    decimal MissedR,
    decimal ExpectedR,
    decimal ExpectedRLow,
    int Trades);