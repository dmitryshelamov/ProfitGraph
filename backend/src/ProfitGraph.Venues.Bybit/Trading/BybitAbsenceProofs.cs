using ProfitGraph.Application.Bots.Contracts;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     Proves that a submit Bybit knows neither in open nor in historical orders never took effect (ADR 0030, ADR 0017). Bybit
///     gives no answer that proves it by itself, and its history lags, so the proof waits and asks for independent evidence. All
///     must hold, in this order, and the first that does not stops the proof; a read that fails is a failed check:
///     <list type="number">
///         <item>
///             <description>the engine never saw the venue accept the order, and the time of its place request is known;</description>
///         </item>
///         <item>
///             <description>
///                 that request is at least <see cref="SettleDelay" /> old, long enough for the history to catch up, and not older than
///                 <see cref="MaxAge" />;
///             </description>
///         </item>
///         <item>
///             <description>the symbol's executions since the request show none of the order;</description>
///         </item>
///         <item>
///             <description>
///                 the base coin's wallet balance equals the position the engine expects, so nothing was bought or sold that the engine
///                 does not know.
///             </description>
///         </item>
///     </list>
///     A wallet that holds more or less of the coin than the bot, such as coins of the owner's own, makes the last check fail and
///     leaves the order with the owner. The proof only reads.
/// </summary>
internal sealed class BybitAbsenceProofs(BybitOrderReads reads, TimeProvider clock)
{
    /// <summary>
    ///     How old a place request must be. Bybit's history lag is an open owner check of ADR 0017; a day is far beyond any lag seen.
    /// </summary>
    internal static readonly TimeSpan SettleDelay = TimeSpan.FromHours(24);

    /// <summary>
    ///     The execution list covers seven days at most.
    /// </summary>
    internal static readonly TimeSpan MaxAge = TimeSpan.FromDays(6);

    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(5);

    public async Task<BybitAbsenceProof> Evaluate(BybitBotContext context, ReconcileOrderInternal order, decimal? expectedBase, CancellationToken cancellationToken)
    {
        List<string> passed = ["NotFoundInOpenAndHistoricalOrders"];

        if (order.AcceptedByEngine)
        {
            return new BybitAbsenceProof(passed, "EngineAcceptedOrder");
        }

        if (order.PlaceStartedAt is not { } placed)
        {
            return new BybitAbsenceProof(passed, "PlaceStartUnknown");
        }

        DateTimeOffset now = clock.GetUtcNow();

        if (now - placed > MaxAge)
        {
            return new BybitAbsenceProof(passed, "OrderTooOld");
        }

        if (now - placed < SettleDelay)
        {
            return new BybitAbsenceProof(passed, "SettleDelayNotPassed");
        }

        passed.Add("SettleDelayPassed");

        try
        {
            if (await reads.ExecutedBetween(context, order.ClientOrderId.Value, placed - Skew, now, cancellationToken))
            {
                return new BybitAbsenceProof(passed, "ExecutionFound");
            }

            passed.Add("NoExecutionOfOrder");

            if (expectedBase is not { } expected)
            {
                return new BybitAbsenceProof(passed, "ExpectedPositionUnknown");
            }

            if (await reads.BaseWalletBalance(context, cancellationToken) != expected)
            {
                return new BybitAbsenceProof(passed, "BalanceDiffers");
            }
        }
        catch (BybitOrderUnresolvedException)
        {
            return new BybitAbsenceProof(passed, "ReadFailed");
        }

        passed.Add("BaseBalanceMatchesEngine");

        return new BybitAbsenceProof(passed, Failed: null);
    }
}
