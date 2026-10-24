using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.CreateBot;

/// <summary>
///     Creates a stopped bot. It reads the venue's fee rates with the credential, so it runs outside a transaction.
/// </summary>
public sealed record CreateBotCommand(
    Guid OwnerId,
    Guid StrategyId,
    Guid CredentialId,
    string? Symbol,
    string? Timeframe,
    IReadOnlyDictionary<string, decimal>? Parameters,
    decimal AllocationCap,
    decimal MaxOrderQuote,
    decimal DailyLossLimitQuote,
    bool AllowForeignOrders) : ICommand<BotInternal>, INonTransactionalCommand;