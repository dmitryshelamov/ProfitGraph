using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Accounts.ListVenueAccounts;

/// <summary>
///     Lists the accounts a credential not saved yet can select (ADR 0023, TC5). The fields are every declared field but the
///     account; they are used for this request only and never stored or printed.
/// </summary>
public sealed record ListVenueAccountsQuery(Guid OwnerId, string? Venue, IReadOnlyDictionary<string, string>? Fields) : IQuery<IReadOnlyList<VenueAccountInternal>>
{
    public override string ToString()
    {
        return $"ListVenueAccountsQuery {{ OwnerId = {OwnerId}, Venue = {Venue}, Redacted }}";
    }
}