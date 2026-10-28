using Microsoft.Extensions.Options;

using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Venues.TInvest.Transport;

namespace ProfitGraph.Venues.TInvest.MarketData;

/// <summary>
///     History of a MOEX share completed by <see cref="TInvestSessionGrid" /> (ADR 0023): the share's and the reference
///     share's real candles are read one day around the range, and the share's last real close before it is searched in
///     earlier windows when needed. Schedules are never used for history. It reads with the caller's T-Invest credential.
/// </summary>
internal sealed class TInvestHistoricalCandleSource(TInvestShares shares, TInvestCandleReader reader, IOptions<TInvestOptions> options) : IHistoricalCandleSource
{
    public VenueId Venue => TInvestVenue.VenueId;

    public string Source => TInvestSessionGrid.Policy;

    public async Task<DateTimeOffset?> FindFirst(InstrumentSymbol symbol, Timeframe timeframe, DateTimeOffset to, CancellationToken cancellationToken)
    {
        (_, TInvestShare share) = await Share(symbol, to, to, cancellationToken);

        if (share.FirstMinuteCandle is not { } first)
        {
            return null;
        }

        DateTimeOffset aligned = timeframe.FloorToGrid(first);
        aligned = aligned < first ? aligned + timeframe.Duration : aligned;

        return aligned < to ? aligned : null;
    }

    public async Task<HistoricalCandlesInternal> Get(
        InstrumentSymbol symbol,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        (TInvestToken token, TInvestShare share) = await Share(symbol, from, to, cancellationToken);
        DateTimeOffset paddedFrom = from - options.Value.History.Padding;
        DateTimeOffset paddedTo = to + options.Value.History.Padding;
        Dictionary<DateTimeOffset, Candle> candles = await reader.Read(token, share, timeframe, paddedFrom, paddedTo, cancellationToken);
        HashSet<DateTimeOffset> reference = await Reference(token, share, timeframe, paddedFrom, paddedTo, candles, cancellationToken);
        decimal? priorClose = candles.Keys.Any(time => time < from && !TInvestSessionGrid.IsWeekend(time))
            ? null
            : await PriorClose(token, share, timeframe, paddedFrom, cancellationToken);

        return TInvestSessionGrid.Build(timeframe, from, to, paddedFrom, paddedTo, candles, reference, priorClose);
    }

    private async Task<(TInvestToken Token, TInvestShare Share)> Share(
        InstrumentSymbol symbol,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        TInvestToken token = TInvestMarketDataToken.Current()
            ?? throw Failure(HistoricalDataErrorCodes.Unavailable, "T-Invest history is read with your T-Invest credential; choose one.", from, to);

        if (!TInvestSymbol.TryParse(symbol.Value, out TInvestSymbol? parsed))
        {
            throw Failure(HistoricalDataErrorCodes.InvalidRequest, "The symbol is not a T-Invest share.", from, to);
        }

        try
        {
            return (token, await shares.Get(token, parsed, cancellationToken));
        }
        catch (InstrumentRulesException error)
        {
            throw Failure(
                error.Code switch
                {
                    InstrumentRulesErrorCodes.InvalidRequest => HistoricalDataErrorCodes.InvalidRequest,
                    InstrumentRulesErrorCodes.InvalidProviderData => HistoricalDataErrorCodes.InvalidProviderData,
                    _ => HistoricalDataErrorCodes.ProviderUnavailable
                },
                error.Message,
                from,
                to);
        }
    }

    private async Task<HashSet<DateTimeOffset>> Reference(
        TInvestToken token,
        TInvestShare share,
        Timeframe timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        Dictionary<DateTimeOffset, Candle> own,
        CancellationToken cancellationToken)
    {
        TInvestSymbol.TryParse(options.Value.History.ReferenceSymbol, out TInvestSymbol? symbol);

        if (symbol == share.Symbol)
        {
            return [.. own.Keys];
        }

        (_, TInvestShare reference) = await Share(symbol!.Symbol, from, to, cancellationToken);

        return [.. (await reader.Read(token, reference, timeframe, from, to, cancellationToken)).Keys];
    }

    /// <summary>
    ///     The share's last real close in the windows before <paramref name="before" />, or null when none has one.
    /// </summary>
    private async Task<decimal?> PriorClose(TInvestToken token, TInvestShare share, Timeframe timeframe, DateTimeOffset before, CancellationToken cancellationToken)
    {
        TimeSpan window = TInvestCandleWindows.For(timeframe).Window;

        for (var step = 0; step < options.Value.History.PriorCloseWindows; step++)
        {
            DateTimeOffset to = before - window * step;
            Dictionary<DateTimeOffset, Candle> candles = await reader.Read(token, share, timeframe, to - window, to, cancellationToken);
            Candle? last = candles.Values.Where(candle => !TInvestSessionGrid.IsWeekend(candle.OpenTime)).MaxBy(candle => candle.OpenTime);

            if (last is not null)
            {
                return last.Close;
            }
        }

        return null;
    }

    private static HistoricalDataException Failure(string code, string message, DateTimeOffset from, DateTimeOffset to)
    {
        return new HistoricalDataException(code, message, [new CandleRangeInternal(from, to)]);
    }
}