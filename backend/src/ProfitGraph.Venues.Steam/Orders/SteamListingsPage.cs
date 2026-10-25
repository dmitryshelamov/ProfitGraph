namespace ProfitGraph.Venues.Steam.Orders;

internal sealed record SteamListingsPage(IReadOnlyList<SteamListing> Listings, int Total, int PageSize);