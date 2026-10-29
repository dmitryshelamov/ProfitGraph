using System.Globalization;

using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     Definitive broker rejections by their numeric code (Profit Fox Designer ADR 0079). A known code maps to its reason,
///     an unknown one to <see cref="RejectionReason.BrokerRejected" />; the message is fixed and carries only the code.
/// </summary>
internal static class TInvestRejections
{
    public static ExternalOrderUpdate For(long engineOrderId, int code, OrderSide side)
    {
        RejectionReason reason = code switch
        {
            30034 => side == OrderSide.Buy ? RejectionReason.InsufficientQuote : RejectionReason.InsufficientBase,
            30015 or 30016 or 30080 => RejectionReason.InvalidAmount,
            30017 or 30018 or 30100 or 30078 => RejectionReason.InvalidPrice,
            30052 or 30079 => RejectionReason.InstrumentUnavailable,
            30042 => RejectionReason.RiskLimit,
            30051 => RejectionReason.MarginTradingUnavailable,
            30057 => RejectionReason.DuplicateRequest,
            _ => RejectionReason.BrokerRejected
        };

        return ExternalOrderUpdate.Reject(engineOrderId, reason, string.Create(CultureInfo.InvariantCulture, $"T-Invest rejected the order (code {code})."));
    }
}