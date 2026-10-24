using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

internal static class MetricsReferenceCandleSnapshotGenerator
{
    internal static CandleSnapshot Generate(TradingAccount account, int candleIndex, decimal open, decimal close)
    {
        DateTimeOffset openTime = DateTimeOffset.UnixEpoch.AddHours(candleIndex);
        var candle = new Candle(openTime, open, Math.Max(open, close), Math.Min(open, close), close, Volume: 1m);
        var context = new ReachedMarketContext(candle, candleIndex, openTime.AddHours(1), PricePhase.Close, close, candleIndex + 1);

        return new CandleSnapshot(candleIndex + 1L, context, AccountSnapshot.Capture(account));
    }
}