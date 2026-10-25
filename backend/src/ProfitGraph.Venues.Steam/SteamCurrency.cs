namespace ProfitGraph.Venues.Steam;

/// <summary>
///     A wallet currency ProfitGraph trades in: its code in instrument symbols, Steam's currency number and its minor unit
///     (ADR 0022). Only KZT has been observed on a real wallet; the others are conservative local mappings.
/// </summary>
internal sealed record SteamCurrency(string Code, int SteamId, decimal MinorUnit, string Prefix, string Suffix)
{
    /// <summary>
    ///     The affixes are how Steam writes prices of the currency in its history; only KZT's was observed.
    /// </summary>
    public static readonly IReadOnlyList<SteamCurrency> Supported =
    [
        new SteamCurrency("USD", SteamId: 1, MinorUnit: 0.01m, "$", ""),
        new SteamCurrency("EUR", SteamId: 3, MinorUnit: 0.01m, "", "€"),
        new SteamCurrency("RUB", SteamId: 5, MinorUnit: 0.01m, "", "руб."),
        new SteamCurrency("KZT", SteamId: 37, MinorUnit: 0.01m, "", "₸")
    ];

    /// <summary>
    ///     Decimal places of the minor unit, which are the quote precision of every item priced in this currency.
    /// </summary>
    public int Precision => MinorUnit.Scale;

    public static SteamCurrency? FindBySteamId(int steamId)
    {
        return Supported.FirstOrDefault(currency => currency.SteamId == steamId);
    }

    public static SteamCurrency? Find(string code)
    {
        return Supported.FirstOrDefault(currency => string.Equals(currency.Code, code, StringComparison.OrdinalIgnoreCase));
    }
}