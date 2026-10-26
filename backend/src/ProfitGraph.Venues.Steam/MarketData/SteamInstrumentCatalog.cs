using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Options;

using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam.MarketData;

/// <summary>
///     Finds Steam items with the market search (<c>/market/search/render/?norender=1</c>) as the market-data account sees
///     them, priced in its wallet currency. A query may start with a currency and an App ID (<c>KZT:570:arcana</c>, the
///     currency is ignored) or with an App ID (<c>570:arcana</c>); otherwise
///     <see cref="SteamOptions.DefaultAppId" /> is searched. An empty query lists the app's most traded items.
/// </summary>
internal sealed class SteamInstrumentCatalog(SteamMarketDataAccount account, IOptions<SteamOptions> options) : IInstrumentCatalog
{
    public VenueId Venue => SteamVenue.VenueId;

    public async Task<IReadOnlyList<InstrumentInternal>> Search(string query, int limit, CancellationToken cancellationToken)
    {
        (int appId, string text) = Split(query.Trim());

        try
        {
            SteamWallet wallet = await account.Wallet(cancellationToken);
            var path = string.Create(
                CultureInfo.InvariantCulture,
                $"/market/search/render/?appid={appId}&query={Uri.EscapeDataString(text)}&count={limit}&norender=1&search_descriptions=0");
            byte[] body = await account.Read(path, SteamRequestClass.Quote, cancellationToken);

            return Parse(body, wallet.Currency, appId, limit);
        }
        catch (SteamMarketDataException exception)
        {
            throw new InstrumentRulesException(
                exception.Unavailable ? InstrumentRulesErrorCodes.ProviderUnavailable : InstrumentRulesErrorCodes.InvalidProviderData,
                exception.Message);
        }
    }

    private (int AppId, string Text) Split(string query)
    {
        // A query typed as a whole symbol starts with its currency, which the wallet decides anyway.
        if (query.Length > 4 && query[3] == ':' && SteamCurrency.Find(query[..3]) is not null)
        {
            query = query[4..].Trim();
        }

        int colon = query.IndexOf(value: ':', StringComparison.Ordinal);

        return colon > 0 && int.TryParse(query[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out int appId) && appId > 0
            ? (appId, query[(colon + 1)..].Trim())
            : (options.Value.DefaultAppId, query);
    }

    private static IReadOnlyList<InstrumentInternal> Parse(byte[] body, SteamCurrency currency, int appId, int limit)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("results", out JsonElement results) || results.ValueKind != JsonValueKind.Array)
            {
                throw new SteamMarketDataException(unavailable: false, "Steam's market search is unreadable.");
            }

            var found = new List<InstrumentInternal>();

            foreach (JsonElement result in results.EnumerateArray())
            {
                if (result.TryGetProperty("hash_name", out JsonElement name) && name.ValueKind == JsonValueKind.String &&
                    SteamInstrument.TryParse($"{currency.Code}:{appId}:{name.GetString()}", out SteamInstrument? instrument))
                {
                    found.Add(new InstrumentInternal(
                            SteamVenue.VenueId.Value,
                            instrument.Symbol.Value,
                            instrument.MarketHashName,
                            currency.Code,
                            SteamItemAppearanceParser.Parse(result)));
                }
            }

            return [.. found.Take(limit)];
        }
        catch (JsonException)
        {
            throw new SteamMarketDataException(unavailable: false, "Steam's market search is unreadable.");
        }
    }
}