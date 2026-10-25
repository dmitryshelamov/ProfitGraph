namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     A buy order's counters as <c>getbuyorderstatus</c> reports them: whole items ordered and still wanted.
/// </summary>
internal sealed record SteamBuyOrderStatus(bool Active, int Quantity, int Remaining)
{
    public int Purchased => Quantity - Remaining;
}