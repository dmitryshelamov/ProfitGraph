using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.RecheckUnresolvedOrders;

/// <summary>
///     Checks again a bot that should run and stopped on an unresolved order (ADR 0030). When the venue's gateway would now
///     close every such order on its own evidence, the bot is returned to Created so the runtime takes it and its
///     reconciliation closes them; otherwise it stays stopped. Returns whether the bot was returned.
/// </summary>
public sealed record RecheckUnresolvedOrdersCommand(Guid BotId) : ICommand<bool>;
