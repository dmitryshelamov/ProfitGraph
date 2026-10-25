using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Venues.TInvest;

/// <summary>
///     A MOEX share as <c>{classCode}:{ticker}</c>, for example <c>TQBR:SBER</c> (ADR 0023). Input is trimmed and
///     upper-cased; a bare ticker means the main board <c>TQBR</c>, the only board supported. The Invest API names the same
///     share <c>SBER_TQBR</c>.
/// </summary>
internal sealed partial record TInvestSymbol(string ClassCode, string Ticker)
{
    public const string MainBoard = "TQBR";

    public InstrumentSymbol Symbol => new InstrumentSymbol($"{ClassCode}:{Ticker}");

    /// <summary>
    ///     The share's identifier in Invest API requests.
    /// </summary>
    public string InstrumentId => $"{Ticker}_{ClassCode}";

    public static bool TryParse(string? input, [NotNullWhen(true)] out TInvestSymbol? symbol)
    {
        Match match = Pattern().Match(input?.Trim().ToUpperInvariant() ?? string.Empty);
        symbol = match.Success ? new TInvestSymbol(MainBoard, match.Groups["ticker"].Value) : null;

        return symbol is not null;
    }

    [GeneratedRegex("^(?:TQBR:)?(?<ticker>[A-Z0-9]{1,12})$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}