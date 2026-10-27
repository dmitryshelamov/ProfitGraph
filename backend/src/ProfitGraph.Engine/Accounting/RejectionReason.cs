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
    CancelledBeforeSend,

    /// <summary>
    ///     The venue's gateway proved on its own evidence that the venue never took the order (ADR 0030).
    /// </summary>
    AbsenceProven
}