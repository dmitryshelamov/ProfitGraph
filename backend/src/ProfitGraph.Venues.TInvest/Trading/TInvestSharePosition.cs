namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     The account's position in the share, in shares: what is free and what working orders block.
/// </summary>
internal sealed record TInvestSharePosition(long Free, long Blocked)
{
    public long Total => Free + Blocked;
}
