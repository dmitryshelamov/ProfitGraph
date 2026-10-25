namespace ProfitGraph.Venues.TInvest;

/// <summary>
///     How T-Invest history is completed (ADR 0023): the reference share whose trades mark the market open, and how many
///     earlier request windows are searched for a share's last real close before a range.
/// </summary>
public sealed class TInvestHistoryOptions
{
    public string ReferenceSymbol { get; init; } = "TQBR:SBER";

    public int PriorCloseWindows { get; init; } = 14;

    internal bool IsValid()
    {
        return TInvestSymbol.TryParse(ReferenceSymbol, out _) && PriorCloseWindows is > 0 and <= 100;
    }
}