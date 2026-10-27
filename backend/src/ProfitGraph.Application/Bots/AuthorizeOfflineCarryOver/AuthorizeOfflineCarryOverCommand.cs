using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.AuthorizeOfflineCarryOver;

/// <summary>
///     Whether a venue module may carry a bot's working day orders over the end of their trading day although no runner reads
///     the bot's orders now (ADR 0030): the owner wants the bot to run, nothing holds it for a safety reason, and no runtime
///     holds its lease. A bot the owner stopped keeps its orders as they are.
/// </summary>
public sealed record AuthorizeOfflineCarryOverCommand(Guid BotId) : ICommand<bool>;
