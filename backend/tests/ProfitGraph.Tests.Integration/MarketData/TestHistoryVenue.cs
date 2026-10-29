using System.Diagnostics.CodeAnalysis;

using ProfitGraph.Application.MarketData.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Tests.Integration.MarketData;

/// <summary>
///     A venue for history tests: upper-case letters and digits, hourly and daily candles.
/// </summary>
public sealed class TestHistoryVenue(string id) : IVenueDescriptor
{
    public const string WithHistory = "test-history";
    public const string WithoutHistory = "test-no-history";
    public const string WithAccounts = "test-accounts";

    public VenueId Id { get; } = new VenueId(id);

    public string Name => "Test venue";

    public IReadOnlyList<Timeframe> Timeframes { get; } = [Timeframe.Parse("1h"), Timeframe.Parse("1d")];

    public bool ConfirmsOrderAbsence => true;

    /// <summary>
    ///     Whether strategies may sell first here, as on futures (ADR 0061); off unless a test turns it on.
    /// </summary>
    public bool SupportsShort { get; init; }

    public bool TryParseSymbol(string? input, [NotNullWhen(true)] out InstrumentSymbol? symbol)
    {
        symbol = input is { Length: > 0 and <= 32 } && input.All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character))
            ? new InstrumentSymbol(input)
            : null;

        return symbol is not null;
    }
}