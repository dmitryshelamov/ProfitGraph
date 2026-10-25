using ProfitGraph.Application.Accounts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Tests.Integration.MarketData;

/// <summary>
///     Accounts of the test account venue: an eligible <c>1000123456</c> and a read-only <c>2000654321</c> unless a test
///     replaces them. The secret <c>refused</c> is rejected and <c>down</c> makes the venue unavailable.
/// </summary>
public sealed class FakeAccountDirectory : IVenueAccountDirectory
{
    public IReadOnlyList<VenueAccountInternal> Accounts { get; set; } = Default();

    public int Calls { get; private set; }
    public VenueId Venue { get; } = new VenueId(TestHistoryVenue.WithAccounts);

    public string AccountField => TestAccountCredentialSchema.Account;

    public Task<IReadOnlyList<VenueAccountInternal>> List(IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken)
    {
        Calls++;

        return fields[TestAccountCredentialSchema.Secret] switch
        {
            "refused" => throw new ApplicationValidationException([new InputError("invalid_venue_credential", "Fields.secret", "Refused.")]),
            "down" => throw new VenueAccountsUnavailableException(),
            _ => Task.FromResult(Accounts)
        };
    }

    public void Reset()
    {
        Accounts = Default();
        Calls = 0;
    }

    private static IReadOnlyList<VenueAccountInternal> Default()
    {
        return
        [
            new VenueAccountInternal("1000123456", "Main", "broker", Eligible: true, Ineligibility: null),
            new VenueAccountInternal("2000654321", "Watch", "broker", Eligible: false, "not_full_access")
        ];
    }
}