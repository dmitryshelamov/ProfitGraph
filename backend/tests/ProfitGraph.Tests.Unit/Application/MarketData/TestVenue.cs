using System.Diagnostics.CodeAnalysis;

using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Tests.Unit.Application.MarketData;

/// <summary>
///     A venue for core tests: upper-case letters and digits, hourly and daily candles.
/// </summary>
internal sealed class TestVenue(string id = "test") : IVenueDescriptor
{
    public VenueId Id { get; } = new VenueId(id);

    public string Name => "Test venue";

    public IReadOnlyList<Timeframe> Timeframes { get; } = [Timeframe.Parse("1h"), Timeframe.Parse("1d")];

    public bool TryParseSymbol(string? input, [NotNullWhen(true)] out InstrumentSymbol? symbol)
    {
        symbol = input is { Length: > 0 } && input.All(char.IsAsciiLetterUpper) ? new InstrumentSymbol(input) : null;

        return symbol is not null;
    }
}