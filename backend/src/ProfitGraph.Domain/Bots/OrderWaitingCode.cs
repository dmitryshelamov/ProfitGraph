namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Why a venue holds a bot's working order locally instead of at the venue (ADR 0022, ADR 0023, TC4). The engine sees
///     such an order as working; the code only explains it. Texts are made at the boundary.
/// </summary>
public enum OrderWaitingCode
{
    PriceBand,
    SessionClosed,
    CarryOver,
    SettlementHold,
    SingleBuyOrderSlot,
    AwaitingConfirmation,
    RateLimited
}