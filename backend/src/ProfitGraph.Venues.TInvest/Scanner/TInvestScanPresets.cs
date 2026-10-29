using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Venues.TInvest.Scanner;

/// <summary>
///     The built-in scanner presets of T-Invest MOEX shares (ADR 0062), with turnover in roubles. Bybit's thresholds do not fit:
///     its turnover is in USDT, shares move less than coins, the scanner's 24-hour windows span the night and the weekend without
///     trades, so a share's NATR is lower than on a market that never closes, and T-Invest gives no spread. «Импульс» finds an hour
///     up at least 2% on volume three times its usual with 30 million roubles a day. «Под лесенку» finds a range (efficiency ratio at
///     most 0.3) with hourly swings of at least 0.35% (a round trip costs 0.1% on the «Трейдер» tariff, 0.6% on «Инвестор»), 100
///     million roubles a day, at most 5% down in a day and 15% below the week's high. The thresholds are a starting point the
///     owner tunes by copying a preset.
/// </summary>
internal static class TInvestScanPresets
{
    internal static readonly IReadOnlyList<ScanPresetInternal> All =
    [
        new ScanPresetInternal(
            Id: null,
            TInvestVenue.VenueId.Value,
            "Импульс",
            [
                new ScanConditionInternal("Change1h", Minimum: 2m, Maximum: null), new ScanConditionInternal("VolumeSpike", Minimum: 3m, Maximum: null),
                new ScanConditionInternal("Turnover24h", Minimum: 30_000_000m, Maximum: null)
            ],
            "Change1h",
            SortDescending: true,
            HideStale: true,
            BuiltIn: true,
            AlertsEnabled: false),
        new ScanPresetInternal(
            Id: null,
            TInvestVenue.VenueId.Value,
            "Под лесенку",
            [
                new ScanConditionInternal("EfficiencyRatio24h", Minimum: null, Maximum: 0.3m), new ScanConditionInternal("Natr24h", Minimum: 0.35m, Maximum: null),
                new ScanConditionInternal("Turnover24h", Minimum: 100_000_000m, Maximum: null), new ScanConditionInternal("Change24h", Minimum: -5m, Maximum: null),
                new ScanConditionInternal("DistanceToHigh7d", Minimum: -15m, Maximum: null)
            ],
            "Natr24h",
            SortDescending: true,
            HideStale: true,
            BuiltIn: true,
            AlertsEnabled: false)
    ];
}