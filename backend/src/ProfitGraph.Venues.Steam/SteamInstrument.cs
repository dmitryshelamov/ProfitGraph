using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Venues.Steam;

/// <summary>
///     A Steam Community Market item priced in one wallet currency: <c>{currency}:{appId}:{market_hash_name}</c>, for example
///     <c>KZT:730:Dreams &amp; Nightmares Case</c> (ADR 0022). The item is the base asset and the currency the quote asset. The
///     hash name is kept exactly as Steam writes it: never case-folded, normalized or trimmed inside.
/// </summary>
internal sealed record SteamInstrument(SteamCurrency Currency, int AppId, string MarketHashName)
{
    public InstrumentSymbol Symbol => new InstrumentSymbol(Text);

    private string Text => string.Create(CultureInfo.InvariantCulture, $"{Currency.Code}:{AppId}:{MarketHashName}");

    /// <summary>
    ///     Reads a symbol as people type it: the currency in any case, the App ID as a positive decimal number, the hash name
    ///     after the second colon as is.
    /// </summary>
    public static bool TryParse(string? input, [NotNullWhen(true)] out SteamInstrument? instrument)
    {
        instrument = null;
        string[] parts = input?.Split(separator: ':', count: 3) ?? [];

        if (parts.Length != 3 || SteamCurrency.Find(parts[0].Trim()) is not { } currency)
        {
            return false;
        }

        string appId = parts[1].Trim();

        if (appId.Length == 0 || !appId.All(char.IsAsciiDigit) ||
            !int.TryParse(appId, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number <= 0)
        {
            return false;
        }

        string name = parts[2];
        var candidate = new SteamInstrument(currency, number, name);

        // The symbol's own limits: no edge or repeated spaces, no other whitespace or control characters, bounded length.
        if (name.Length == 0 || name[0] == ' ' || name[^1] == ' ' || name.Contains("  ", StringComparison.Ordinal) ||
            name.Any(character => char.IsControl(character) || (char.IsWhiteSpace(character) && character != ' ')) ||
            candidate.Text.Length > InstrumentSymbol.MaxLength)
        {
            return false;
        }

        instrument = candidate;

        return true;
    }
}