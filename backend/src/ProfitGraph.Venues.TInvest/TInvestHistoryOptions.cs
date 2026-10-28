namespace ProfitGraph.Venues.TInvest;

/// <summary>
///     How T-Invest history is completed (ADR 0023): the reference share whose trades mark the market open, how many earlier
///     request windows are searched for a share's last real close before a range, and how far a range is widened on both
///     sides before it is read.
/// </summary>
public sealed class TInvestHistoryOptions
{
    public string ReferenceSymbol { get; init; } = "TQBR:SBER";

    public int PriorCloseWindows { get; init; } = 14;

    public TimeSpan Padding { get; init; } = TimeSpan.FromDays(1);

    internal bool IsValid()
    {
        return TInvestSymbol.TryParse(ReferenceSymbol, out _) && PriorCloseWindows is > 0 and <= 100 && Padding >= TimeSpan.Zero;
    }
}