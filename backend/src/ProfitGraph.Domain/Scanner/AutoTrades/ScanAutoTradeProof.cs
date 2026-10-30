namespace ProfitGraph.Domain.Scanner.AutoTrades;

/// <summary>
///     What lets a rule trade on paper (ADR 0071, section 5): its cards proved themselves in the latest completed backtest of its preset's
///     conditions and in another one over a period in which the venue's reference pair moved the other way.
/// </summary>
public sealed record ScanAutoTradeProof(ScanAutoTradeEvidence Backtest, ScanAutoTradeEvidence OtherRegime)
{
    public bool Proves(ScanAutoTradeThresholds thresholds)
    {
        return Backtest.BacktestId != OtherRegime.BacktestId && Backtest.MarketChange is { } first && OtherRegime.MarketChange is { } second &&
            Math.Sign(first) * Math.Sign(second) < 0 && Backtest.Proves(thresholds) && OtherRegime.Proves(thresholds);
    }
}