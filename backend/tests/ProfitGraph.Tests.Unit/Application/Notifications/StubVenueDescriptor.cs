using System.Diagnostics.CodeAnalysis;

using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Tests.Unit.Application.Notifications;

/// <summary>
///     A venue descriptor with a fixed name and market, for message texts.
/// </summary>
internal sealed class StubVenueDescriptor(string name, string? market) : IVenueDescriptor
{
    public VenueId Id { get; } = new VenueId("bybit");
    public string Name { get; } = name;
    public IReadOnlyList<Timeframe> Timeframes { get; } = [Timeframe.Parse("1h")];
    public string? Market { get; } = market;

    public bool TryParseSymbol(string? input, [NotNullWhen(true)] out InstrumentSymbol? symbol)
    {
        symbol = null;

        return false;
    }
}