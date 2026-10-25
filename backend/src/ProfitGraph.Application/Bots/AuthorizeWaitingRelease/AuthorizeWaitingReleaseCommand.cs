using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.AuthorizeWaitingRelease;

/// <summary>
///     A venue module asks whether it may now send a bot's order that it held locally (ADR 0023, TC4). The same gates as a
///     submit apply: the bot should run and is healthy, no trading halt, and a live lease whose submit gate is open. A
///     stopped, killed or degraded bot never sends because a band moved or a session opened.
/// </summary>
public sealed record AuthorizeWaitingReleaseCommand(Guid BotId) : ICommand<bool>;