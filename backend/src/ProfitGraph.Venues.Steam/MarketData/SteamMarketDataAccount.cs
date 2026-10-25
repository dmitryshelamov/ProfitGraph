using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam.MarketData;

/// <summary>
///     Reads Steam's market data through the session of the configured market-data credential
///     (<see cref="SteamOptions.MarketDataCredentialId" />), at the lowest priorities of its account queue. A wallet is
///     reused for <see cref="SteamOptions.WalletReuse" /> and an item's history, which one response holds whole, for
///     <see cref="SteamOptions.HistoryReuse" />; callers asking for the same item at once share one read.
/// </summary>
internal sealed class SteamMarketDataAccount(SteamAccountSessions sessions, IServiceScopeFactory scopes, IOptions<SteamOptions> options, TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<(SteamPriceHistory History, DateTimeOffset ReadAt)>>> _histories =
        new ConcurrentDictionary<string, Lazy<Task<(SteamPriceHistory History, DateTimeOffset ReadAt)>>>(StringComparer.Ordinal);

    private (SteamWallet Wallet, DateTimeOffset ReadAt)? _wallet;

    public async Task<SteamWallet> Wallet(CancellationToken cancellationToken)
    {
        if (_wallet is { } cached && clock.GetUtcNow() - cached.ReadAt < options.Value.WalletReuse)
        {
            return cached.Wallet;
        }

        SteamAccountSession session = await Session(cancellationToken);
        SteamWallet wallet = SteamWalletParser.Read(
            Body(await session.Execute(SteamHttpRequest.Get(SteamEndpoint.Community, "/market/", SteamRequestClass.Account), cancellationToken)));
        _wallet = (wallet, clock.GetUtcNow());

        return wallet;
    }

    /// <summary>
    ///     The item's whole history in the account's wallet currency, which must be the instrument's.
    /// </summary>
    public async Task<SteamPriceHistory> History(SteamInstrument instrument, CancellationToken cancellationToken)
    {
        SteamWallet wallet = await Wallet(cancellationToken);

        if (wallet.Currency != instrument.Currency)
        {
            throw new SteamMarketDataException(unavailable: false, $"The market-data account's wallet is in {wallet.Currency.Code}, not {instrument.Currency.Code}.");
        }

        string key = instrument.Symbol.Value;

        while (true)
        {
            Lazy<Task<(SteamPriceHistory History, DateTimeOffset ReadAt)>> read = _histories.GetOrAdd(
                key,
                _ => new Lazy<Task<(SteamPriceHistory History, DateTimeOffset ReadAt)>>(() => ReadHistory(instrument, wallet.Currency)));

            try
            {
                (SteamPriceHistory history, DateTimeOffset readAt) = await read.Value.WaitAsync(cancellationToken);

                if (clock.GetUtcNow() - readAt < options.Value.HistoryReuse)
                {
                    return history;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _histories.TryRemove(new KeyValuePair<string, Lazy<Task<(SteamPriceHistory History, DateTimeOffset ReadAt)>>>(key, read));

                throw;
            }

            _histories.TryRemove(new KeyValuePair<string, Lazy<Task<(SteamPriceHistory History, DateTimeOffset ReadAt)>>>(key, read));
        }
    }

    public async Task<byte[]> Read(string path, SteamRequestClass requestClass, CancellationToken cancellationToken)
    {
        SteamAccountSession session = await Session(cancellationToken);

        return Body(await session.Execute(SteamHttpRequest.Get(SteamEndpoint.Community, path, requestClass), cancellationToken));
    }

    private static byte[] Body(SteamRequestResult result)
    {
        return result.Outcome switch
        {
            SteamRequestOutcome.Success => result.Response!.Body,
            SteamRequestOutcome.ClientError or SteamRequestOutcome.InvalidResponse =>
                throw new SteamMarketDataException(unavailable: false, $"Steam refused the market data request ({result.Response?.StatusCode})."),
            _ => throw new SteamMarketDataException(unavailable: true, $"Steam's market data is unavailable now ({result.Outcome}).")
        };
    }

    private async Task<(SteamPriceHistory History, DateTimeOffset ReadAt)> ReadHistory(SteamInstrument instrument, SteamCurrency currency)
    {
        // A shared read must not end when its first caller stops waiting, so it runs without that caller's token.
        var path = $"/market/pricehistory/?appid={instrument.AppId}&market_hash_name={Uri.EscapeDataString(instrument.MarketHashName)}";
        byte[] payload = await Read(path, SteamRequestClass.History, CancellationToken.None);
        DateTimeOffset now = clock.GetUtcNow();

        return (SteamPriceHistoryParser.Read(payload, currency, now, options.Value.CandleCloseDelay), now);
    }

    private async Task<SteamAccountSession> Session(CancellationToken cancellationToken)
    {
        if (options.Value.MarketDataCredentialId is not { } credentialId)
        {
            throw new SteamMarketDataException(unavailable: true, "No Steam account is configured for market data.");
        }

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        ExchangeCredential? credential = await scope.ServiceProvider.GetRequiredService<IExchangeCredentialRepository>()
            .Get(new ExchangeCredentialId(credentialId), cancellationToken);

        if (credential is null || credential.Venue != SteamVenue.VenueId)
        {
            throw new SteamMarketDataException(unavailable: true, "The configured market-data account is not a stored Steam credential.");
        }

        return sessions.For(credential.OwnerId.Value, credentialId);
    }
}