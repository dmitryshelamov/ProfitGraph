using System.Text.Json;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.CreateBot;

/// <summary>
///     Creates a stopped bot. It reads the venue's fee rates with the credential, so it runs outside a transaction.
///     <see cref="VenueSettings" /> is the venue module's own settings object; without it the bot takes the module's defaults.
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
    bool AllowForeignOrders,
    JsonElement? VenueSettings = null) : ICommand<BotInternal>, INonTransactionalCommand;