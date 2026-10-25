namespace ProfitGraph.Api.Accounts.Contracts;

/// <summary>
///     Every field of a venue's credential but the account; used for this request only. Values are never printed.
/// </summary>
public sealed record ListVenueAccountsRequest(string? Venue, IReadOnlyDictionary<string, string>? Fields)
{
    public override string ToString()
    {
        return $"ListVenueAccountsRequest {{ Venue = {Venue}, Redacted }}";
    }
}