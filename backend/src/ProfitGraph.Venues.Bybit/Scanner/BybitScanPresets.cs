using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Venues.Bybit.Scanner;

/// <summary>
///     The two built-in scanner presets of Bybit Spot (ADR 0059, section 4), with turnover in USDT. «Импульс» finds a move now:
///     an hour up at least 3% on volume three times its usual. «Под лесенку» finds pairs for a Ladder: a range rather than a
///     trend (efficiency ratio at most 0.3), hourly swings of at least 0.5% against a round trip of taker fees near 0.2%, liquid,
///     a tight spread, and not falling (at most 5% down in a day, at most 25% below the week's high). The thresholds are a
///     starting point the owner tunes by copying a preset. USDT perpetuals (ADR 0061) have «Импульс» and its mirror «Падение», an
///     hour down at least 3% on volume three times its usual, for short ideas.
/// </summary>
internal static class BybitScanPresets
{
    internal static readonly IReadOnlyList<ScanPresetInternal> All =
    [
        new ScanPresetInternal(
            Id: null,
            BybitVenue.VenueId.Value,
            "Импульс",
            [
                new ScanConditionInternal("Change1h", Minimum: 3m, Maximum: null), new ScanConditionInternal("VolumeSpike", Minimum: 3m, Maximum: null),
                new ScanConditionInternal("Turnover24h", Minimum: 500_000m, Maximum: null)
            ],
            "Change1h",
            SortDescending: true,
            HideStale: true,
            BuiltIn: true,
            AlertsEnabled: false),
        new ScanPresetInternal(
            Id: null,
            BybitVenue.VenueId.Value,
            "Под лесенку",
            [
                new ScanConditionInternal("EfficiencyRatio24h", Minimum: null, Maximum: 0.3m), new ScanConditionInternal("Natr24h", Minimum: 0.5m, Maximum: null),
                new ScanConditionInternal("Turnover24h", Minimum: 1_000_000m, Maximum: null), new ScanConditionInternal("Spread", Minimum: null, Maximum: 0.1m),
                new ScanConditionInternal("Change24h", Minimum: -5m, Maximum: null), new ScanConditionInternal("DistanceToHigh7d", Minimum: -25m, Maximum: null)
            ],
            "Natr24h",
            SortDescending: true,
            HideStale: true,
            BuiltIn: true,
            AlertsEnabled: false)
    ];

    internal static readonly IReadOnlyList<ScanPresetInternal> Linear =
    [
        All[0] with { Venue = BybitMarket.Linear.Venue.Value, Conditions = [.. All[0].Conditions.Select(Liquid)] },
        new ScanPresetInternal(
            Id: null,
            BybitMarket.Linear.Venue.Value,
            "Падение",
            [
                new ScanConditionInternal("Change1h", Minimum: null, Maximum: -3m), new ScanConditionInternal("VolumeSpike", Minimum: 3m, Maximum: null),
                new ScanConditionInternal("Turnover24h", Minimum: 1_000_000m, Maximum: null)
            ],
            "Change1h",
            SortDescending: false,
            HideStale: true,
            BuiltIn: true,
            AlertsEnabled: false)
    ];

    internal static IReadOnlyList<ScanPresetInternal> For(BybitMarket market)
    {
        return market.IsLinear ? Linear : All;
    }

    // Futures turnover runs far above spot's; the linear «Импульс» asks for a million a day.
    private static ScanConditionInternal Liquid(ScanConditionInternal condition)
    {
        return condition.Metric == "Turnover24h" ? condition with { Minimum = 1_000_000m } : condition;
    }
}