using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Describes every account of the test venue with an invented UID and counts the reads (ADR 0049).
/// </summary>
internal sealed class FakeAccountIdentityReader : IVenueAccountIdentityReader
{
    private int _reads;

    public int Reads => Volatile.Read(ref _reads);

    public VenueId Venue => new VenueId(TestHistoryVenue.WithHistory);

    public Task<AccountIdentityReadInternal> Read(ExchangeCredentialSecretsInternal credential, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _reads);

        return Task.FromResult(
            AccountIdentityReadInternal.Observed(
                new AccountIdentityInternal("invented-uid", "invented", Kind: null, AccessLevel: null, ReadOnly: null, [], ExpiresAt: null)));
    }
}