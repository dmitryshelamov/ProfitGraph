using System.Collections.Concurrent;

using ProfitGraph.Application.Bots.Contracts;

namespace ProfitGraph.Application.Scanner;

/// <summary>
///     The venue's fee rates the cards last read for a credential and a pair (ADR 0065), kept for <see cref="ScanTradeOptions.FeeRatesLifetime" />
///     so that opening a pair does not ask the venue every time. Shared by every request of the host.
/// </summary>
public sealed class ScanTradeFeeRateCache(TimeProvider clock)
{
    private readonly ConcurrentDictionary<(Guid Credential, string Venue, string Symbol), (VenueFeeRatesInternal Rates, DateTimeOffset ReadAt)> _rates =
        new ConcurrentDictionary<(Guid Credential, string Venue, string Symbol), (VenueFeeRatesInternal Rates, DateTimeOffset ReadAt)>();

    public VenueFeeRatesInternal? Find(Guid credential, string venue, string symbol, TimeSpan lifetime)
    {
        return _rates.TryGetValue((credential, venue, symbol), out (VenueFeeRatesInternal Rates, DateTimeOffset ReadAt) entry) &&
            clock.GetUtcNow() - entry.ReadAt < lifetime
                ? entry.Rates
                : null;
    }

    public void Store(Guid credential, string venue, string symbol, VenueFeeRatesInternal rates)
    {
        _rates[(credential, venue, symbol)] = (rates, clock.GetUtcNow());
    }
}