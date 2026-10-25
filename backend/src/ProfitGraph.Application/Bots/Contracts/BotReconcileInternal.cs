using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     What a venue checks when a bot opens: the bot, whether it tolerates foreign orders on its symbol and every order of
///     its engine and intents whose venue state is not known.
/// </summary>
public sealed record BotReconcileInternal(BotId BotId, bool AllowForeignOrders, IReadOnlyList<ReconcileOrderInternal> Orders);