using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.Scanner;

namespace ProfitGraph.Venues.Bybit.Scanner;

/// <summary>
///     The built-in scanner presets of Bybit (ADR 0059, section 4), with turnover in USDT; spot and USDT perpetuals have the same five
///     (ADR 0068). «Импульс» finds a move now: an hour up at least 3% on volume three times its usual; «Падение» is its mirror, an hour
///     down at least 3%. «Ранний импульс» (ADR 0066) finds the start of a move: 15 minutes up at least 2% on a 15-minute volume spike
///     of at least 3, above the high of the range before, an hour at most 6% up; «Раннее падение» is its mirror, below the range's
///     low. «Под лесенку» finds pairs for a Ladder: a range rather than a trend (efficiency ratio at most 0.3), hourly swings of at
///     least 0.5% against a round trip of taker fees near 0.2%, liquid, a tight spread, and not falling (at most 5% down in a day, at
///     most 25% below the week's high). Futures turnover runs far above spot's, so the perpetuals ask for twice the turnover. Every
///     preset asks for a spread of at most 0.1%, which a stopped trade crosses on top of its fees (ADR 0065). The thresholds are a
///     starting point the owner tunes by copying a preset; the falls and the perpetuals' presets reuse the spot thresholds the preset
///     backtest chose for the rises. The rises offer long trade cards, the falls short ones (ADR 0069).
/// </summary>
internal static class BybitScanPresets
{
    internal static readonly IReadOnlyList<ScanPresetInternal> All = Presets(BybitVenue.VenueId.Value, turnover: 1m);

    internal static readonly IReadOnlyList<ScanPresetInternal> Linear = Presets(BybitMarket.Linear.Venue.Value, turnover: 2m);

    internal static IReadOnlyList<ScanPresetInternal> For(BybitMarket market)
    {
        return market.IsLinear ? Linear : All;
    }

    private static IReadOnlyList<ScanPresetInternal> Presets(string venue, decimal turnover)
    {
        return
        [
            Preset(
                venue,
                "Импульс",
                [
                    new ScanConditionInternal("Change1h", Minimum: 3m, Maximum: null), new ScanConditionInternal("VolumeSpike", Minimum: 3m, Maximum: null),
                    new ScanConditionInternal("Turnover24h", Minimum: 500_000m * turnover, Maximum: null), new ScanConditionInternal("Spread", Minimum: null, Maximum: 0.1m)
                ],
                "Change1h",
                descending: true,
                ScanTradeDirection.Long),
            Preset(
                venue,
                "Ранний импульс",
                [
                    new ScanConditionInternal("Change15m", Minimum: 2m, Maximum: null),
                    new ScanConditionInternal("VolumeSpike15m", Minimum: 3m, Maximum: null),
                    new ScanConditionInternal("Breakout4h", Minimum: 0m, Maximum: null), new ScanConditionInternal("Change1h", Minimum: null, Maximum: 6m),
                    new ScanConditionInternal("Turnover24h", Minimum: 500_000m * turnover, Maximum: null), new ScanConditionInternal("Spread", Minimum: null, Maximum: 0.1m)
                ],
                "Change15m",
                descending: true,
                ScanTradeDirection.Long),
            Preset(
                venue,
                "Падение",
                [
                    new ScanConditionInternal("Change1h", Minimum: null, Maximum: -3m), new ScanConditionInternal("VolumeSpike", Minimum: 3m, Maximum: null),
                    new ScanConditionInternal("Turnover24h", Minimum: 500_000m * turnover, Maximum: null), new ScanConditionInternal("Spread", Minimum: null, Maximum: 0.1m)
                ],
                "Change1h",
                descending: false,
                ScanTradeDirection.Short),
            Preset(
                venue,
                "Раннее падение",
                [
                    new ScanConditionInternal("Change15m", Minimum: null, Maximum: -2m),
                    new ScanConditionInternal("VolumeSpike15m", Minimum: 3m, Maximum: null),
                    new ScanConditionInternal("Breakdown4h", Minimum: null, Maximum: 0m), new ScanConditionInternal("Change1h", Minimum: -6m, Maximum: null),
                    new ScanConditionInternal("Turnover24h", Minimum: 500_000m * turnover, Maximum: null), new ScanConditionInternal("Spread", Minimum: null, Maximum: 0.1m)
                ],
                "Change15m",
                descending: false,
                ScanTradeDirection.Short),
            Preset(
                venue,
                "Под лесенку",
                [
                    new ScanConditionInternal("EfficiencyRatio24h", Minimum: null, Maximum: 0.3m), new ScanConditionInternal("Natr24h", Minimum: 0.5m, Maximum: null),
                    new ScanConditionInternal("Turnover24h", Minimum: 1_000_000m * turnover, Maximum: null), new ScanConditionInternal("Spread", Minimum: null, Maximum: 0.1m),
                    new ScanConditionInternal("Change24h", Minimum: -5m, Maximum: null), new ScanConditionInternal("DistanceToHigh7d", Minimum: -25m, Maximum: null)
                ],
                "Natr24h",
                descending: true,
                ScanTradeDirection.Both)
        ];
    }

    private static ScanPresetInternal Preset(
        string venue,
        string name,
        IReadOnlyList<ScanConditionInternal> conditions,
        string sort,
        bool descending,
        ScanTradeDirection trades)
    {
        return new ScanPresetInternal(
            Id: null,
            venue,
            name,
            conditions,
            sort,
            descending,
            HideStale: true,
            BuiltIn: true,
            AlertsEnabled: false,
            trades);
    }
}
