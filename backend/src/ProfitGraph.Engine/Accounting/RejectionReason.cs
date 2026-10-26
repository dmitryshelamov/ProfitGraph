namespace ProfitGraph.Engine.Accounting;

public enum RejectionReason
{
    InvalidAmount,
    InvalidPrice,
    ZeroQuantity,
    BelowMinQuantity,
    BelowMinNotional,
    InsufficientQuote,
    InsufficientBase,
    TagActive,
    OwnerConfirmedAbsent,
    RiskLimit,
    KillSwitch,
    InstrumentUnavailable,
    MarginTradingUnavailable,
    BrokerRejected,
    CancelledBeforeSend
}