using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Venues.Steam.MarketData;

/// <summary>
///     Steam's median history (ADR 0022). Hourly candles exist only from the start of the hourly zone, about the last 30
///     days; an hour or day without sales has no candle, so a replay over it fails as incomplete.
/// </summary>
internal sealed class SteamHistoricalCandleSource(SteamMarketDataAccount account) : IHistoricalCandleSource
{
    public VenueId Venue => SteamVenue.VenueId;

    public string Source => "steam-median-v1";

    public bool RestatesClosedCandles => true;

    public async Task<DateTimeOffset?> FindFirst(InstrumentSymbol symbol, Timeframe timeframe, DateTimeOffset to, CancellationToken cancellationToken)
    {
        IReadOnlyList<Candle> candles = (await History(symbol, cancellationToken)).For(timeframe);

        return candles.Count > 0 && candles[0].OpenTime < to ? candles[0].OpenTime : null;
    }

    public async Task<HistoricalCandlesInternal> Get(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        return HistoricalCandlesInternal.Contiguous(
            [.. (await History(symbol, cancellationToken)).For(timeframe).Where(candle => candle.OpenTime >= from && candle.OpenTime < to)]);
    }

    private async Task<SteamPriceHistory> History(InstrumentSymbol symbol, CancellationToken cancellationToken)
    {
        if (!SteamInstrument.TryParse(symbol.Value, out SteamInstrument? instrument))
        {
            throw new HistoricalDataException(HistoricalDataErrorCodes.InvalidRequest, "The symbol is not a Steam item.", []);
        }

        try
        {
            return await account.History(instrument, cancellationToken);
        }
        catch (SteamMarketDataException exception)
        {
            throw new HistoricalDataException(
                exception.Unavailable ? HistoricalDataErrorCodes.ProviderUnavailable : HistoricalDataErrorCodes.InvalidProviderData,
                exception.Message,
                []);
        }
    }
}