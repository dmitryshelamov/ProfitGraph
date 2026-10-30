using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Scanner.Repositories.Converters;

internal static class ScanSnapshotRowDbConverter
{
    public static ScanSnapshotRowInternal ToInternal(this ScanSnapshotRowDb row)
    {
        return new ScanSnapshotRowInternal(
            row.Symbol,
            new ScanMetrics
            {
                Price = row.Price,
                Change5m = row.Change5m,
                Change15m = row.Change15m,
                Change1h = row.Change1h,
                Change4h = row.Change4h,
                Change24h = row.Change24h,
                Turnover1h = row.Turnover1h,
                Turnover24h = row.Turnover24h,
                VolumeSpike = row.VolumeSpike,
                VolumeSpike5m = row.VolumeSpike5m,
                VolumeSpike15m = row.VolumeSpike15m,
                Natr24h = row.Natr24h,
                EfficiencyRatio24h = row.EfficiencyRatio24h,
                DistanceToHigh24h = row.DistanceToHigh24h,
                DistanceToLow24h = row.DistanceToLow24h,
                DistanceToHigh7d = row.DistanceToHigh7d,
                DistanceToLow7d = row.DistanceToLow7d,
                Breakout4h = row.Breakout4h,
                Breakdown4h = row.Breakdown4h,
                Range4h = row.Range4h,
                Resistance = row.Resistance,
                Support = row.Support,
                DistanceToResistance = row.DistanceToResistance,
                DistanceToSupport = row.DistanceToSupport,
                RewardRisk = row.RewardRisk,
                TrendEma50 = row.TrendEma50,
                EmaSpread20x50 = row.EmaSpread20x50,
                Rsi1h = row.Rsi1h,
                MarketTrend = row.MarketTrend,
                Spread = row.Spread,
                ListingAgeDays = row.ListingAgeDays,
                Stale = row.Stale
            },
            row.Incomplete);
    }
}