using System.Text.Json;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.CreateBot;

/// <summary>
///     Creates a stopped bot. It reads the venue's fee rates with the credential, so it runs outside a transaction.
///     <see cref="VenueSettings" /> is the venue module's own settings object; without it the bot takes the module's defaults.
///     <see cref="Venue" /> is the bot's venue, one that accepts the credential (ADR 0061); without it the bot trades on the
///     credential's own venue.
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
    JsonElement? VenueSettings = null,
    string? Venue = null) : ICommand<BotInternal>, INonTransactionalCommand;