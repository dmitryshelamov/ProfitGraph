using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Application.MarketData;

/// <summary>
///     The venues registered by venue modules, in registration order. An unknown venue is never replaced by a default.
/// </summary>
public sealed class VenueRegistry
{
    public IReadOnlyList<IVenueDescriptor> Venues { get; }

    public VenueRegistry(IEnumerable<IVenueDescriptor> venues)
    {
        Venues = venues.ToArray();

        if (Venues.Select(venue => venue.Id).Distinct().Count() != Venues.Count)
        {
            throw new InvalidOperationException("Every venue module must register a distinct venue identifier.");
        }
    }

    /// <summary>
    ///     Finds the venue named by a request; anything that is not a registered venue identifier gives null.
    /// </summary>
    public IVenueDescriptor? Find(string? venue)
    {
        return VenueId.IsValid(venue) ? Venues.FirstOrDefault(descriptor => descriptor.Id == new VenueId(venue!)) : null;
    }

    /// <summary>
    ///     Resolves the series a request names, or explains which part is wrong with a stable error code.
    /// </summary>
    public IReadOnlyList<InputError> TryResolveSeries(string? venue, string? symbol, string? timeframe, out CandleSeries? series)
    {
        series = null;
        IVenueDescriptor? descriptor = Find(venue);

        if (descriptor is null)
        {
            return [new InputError("unknown_venue", "Venue", "The venue is not registered.")];
        }

        var errors = new List<InputError>();

        if (!descriptor.TryParseSymbol(symbol, out InstrumentSymbol? parsed))
        {
            errors.Add(new InputError("invalid_symbol", "Symbol", $"The symbol is not a valid {descriptor.Name} symbol."));
        }

        Timeframe? resolved = descriptor.Timeframes.FirstOrDefault(item => item.Key == timeframe?.Trim());

        if (resolved is null)
        {
            errors.Add(new InputError("unsupported_timeframe", "Timeframe", $"{descriptor.Name} does not support this timeframe."));
        }

        if (errors.Count == 0)
        {
            series = new CandleSeries(descriptor.Id, parsed!, resolved!);
        }

        return errors;
    }

    /// <summary>
    ///     Resolves a series that the request validator has already accepted.
    /// </summary>
    public CandleSeries ResolveSeries(string? venue, string? symbol, string? timeframe)
    {
        IReadOnlyList<InputError> errors = TryResolveSeries(venue, symbol, timeframe, out CandleSeries? series);

        return series ?? throw new ApplicationValidationException(errors);
    }

    /// <summary>
    ///     Refuses a short plan on a venue that does not allow shorts (ADR 0061); a long plan runs anywhere.
    /// </summary>
    public void EnsureDirection(VenueId venue, PositionDirection direction)
    {
        if (direction == PositionDirection.Short && Find(venue.Value) is not { SupportsShort: true })
        {
            throw new ApplicationValidationException(
                [new InputError("strategy_short_unsupported", "StrategyId", "A short strategy runs only on a venue that allows shorts, such as futures.")]);
        }
    }
}