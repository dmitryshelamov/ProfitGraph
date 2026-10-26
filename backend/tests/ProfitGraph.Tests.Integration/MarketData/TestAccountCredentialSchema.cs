using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Tests.Integration.MarketData;

/// <summary>
///     The test venue whose credential is a secret and the account it selects, like T-Invest's token and account.
/// </summary>
public sealed class TestAccountCredentialSchema : IVenueCredentialSchema
{
    public const string Secret = "secret";
    public const string Account = "accountId";

    public VenueId Venue { get; } = new VenueId(TestHistoryVenue.WithAccounts);

    public IReadOnlyList<string> Fields { get; } = [Account, Secret];

    public string PublicField => Account;
}