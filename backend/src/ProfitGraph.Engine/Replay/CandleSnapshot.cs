using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Immutable completed candle observation with eagerly validated financial values.
/// </summary>
public sealed record CandleSnapshot
{
    public long Sequence { get; }
    public ReachedMarketContext Context { get; }
    public AccountSnapshot Account { get; }
    public decimal Equity { get; }
    public decimal UnrealizedPnl { get; }
    public decimal TotalPnl { get; }

    /// <summary>
    ///     Whether the position was open at this close by the engine's flat rule (ADR 0057).
    /// </summary>
    public bool InPosition { get; }

    public CandleSnapshot(long sequence, ReachedMarketContext context, AccountSnapshot account, bool inPosition = false)
    {
        AccountValuation valuation = AccountValuation.Capture(account, context.MarketPrice);
        Sequence = sequence;
        Context = context;
        Account = account;
        Equity = valuation.Equity;
        UnrealizedPnl = valuation.UnrealizedPnl;
        TotalPnl = valuation.TotalPnl;
        InPosition = inPosition;
    }

    private CandleSnapshot(
        long sequence,
        ReachedMarketContext context,
        AccountSnapshot account,
        decimal equity,
        decimal unrealizedPnl,
        decimal totalPnl,
        bool inPosition)
    {
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        Sequence = sequence;
        Context = context;
        Account = account;
        Equity = equity;
        UnrealizedPnl = unrealizedPnl;
        TotalPnl = totalPnl;
        InPosition = inPosition;
    }

    /// <summary>
    ///     Restores already validated committed financial values without recalculation.
    /// </summary>
    public static CandleSnapshot Restore(
        long sequence,
        ReachedMarketContext context,
        AccountSnapshot account,
        decimal equity,
        decimal unrealizedPnl,
        decimal totalPnl,
        bool inPosition = false)
    {
        return new CandleSnapshot(sequence, context, account, equity, unrealizedPnl, totalPnl, inPosition);
    }
}