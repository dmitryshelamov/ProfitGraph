using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     One attempt to place an engine order at T-Invest (ADR 0023): its deterministic request ID, the share and lots it
///     asks for, its state, the exchange order ID once known and the broker code of a definitive rejection.
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
    DateTimeOffset UpdatedAt)
{
    /// <summary>
    ///     Whether a place request of the attempt may have reached T-Invest.
    /// </summary>
    public bool MayHaveReachedVenue => State != TInvestOrderAttemptState.Planned && !(State == TInvestOrderAttemptState.Final && ExchangeOrderId is null);
}