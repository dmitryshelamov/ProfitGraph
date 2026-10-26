using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Bots;

/// <summary>
///     A bot operation that the live rules forbid, such as starting while trading is halted.
/// </summary>
public sealed class BotRuleException(string code, string message) : DomainException(code, message)
{
    public const string InvalidName = "invalid_bot_name";
    public const string InvalidSettings = "invalid_bot_settings";
    public const string TradingHalted = "trading_halted";
    public const string MustBeStopped = "bot_must_be_stopped";
    public const string NotRunning = "bot_not_running";
    public const string InvalidIntentTransition = "invalid_intent_transition";
    public const string LeaseHeld = "bot_lease_held";
    public const string NotLeasable = "bot_not_leasable";
    public const string ManualOrderResolved = "manual_order_resolved";
}