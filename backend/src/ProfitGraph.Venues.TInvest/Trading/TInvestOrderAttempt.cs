using ProfitGraph.Domain.Bots.OrderWaiting;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     One attempt to place an engine order at T-Invest (ADR 0023): its deterministic request ID, the share and lots it
///     asks for, its state, the exchange order ID once known, the broker code of a definitive rejection and, while it waits
///     locally, why and the band bounds last seen (T12a); who asked to cancel it, the trading date a carried
///     remainder was carried from, and once final
///     its own executed lots, quote and fee, which later attempts of the same engine order add to (T12b).
/// </summary>
internal sealed record TInvestOrderAttempt(
    Guid BotId,
    long EngineOrderId,
    int Attempt,
    Guid RequestId,
    string InstrumentUid,
    OrderSide Side,
    OrderType Type,
    long Lots,
    decimal? Price,
    TInvestOrderAttemptState State,
    string? ExchangeOrderId,
    int? BrokerCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    OrderWaitingCode? WaitingCode = null,
    decimal? BandLow = null,
    decimal? BandHigh = null,
    DateOnly? CarriedFrom = null,
    TInvestCancelReason? CancelledBy = null,
    long ExecutedLots = 0,
    decimal ExecutedQuote = 0m,
    decimal ExecutedFee = 0m)
{
    /// <summary>
    ///     Whether a place request of the attempt may have reached T-Invest.
    /// </summary>
    public bool MayHaveReachedVenue => State != TInvestOrderAttemptState.Planned && !(State == TInvestOrderAttemptState.Final && ExchangeOrderId is null);
}