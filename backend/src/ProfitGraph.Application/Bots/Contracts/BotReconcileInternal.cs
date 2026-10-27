using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     What a venue checks when a bot opens: the bot, whether it tolerates foreign orders on its symbol, every order of
///     its engine and intents whose venue state is not known, and the position of the instrument the engine expects
///     (<see cref="ExpectedBase" />, null without a checkpoint), which a gateway that proves an absence by itself compares
///     with the account's (ADR 0030).
/// </summary>
public sealed record BotReconcileInternal(BotId BotId, bool AllowForeignOrders, IReadOnlyList<ReconcileOrderInternal> Orders, decimal? ExpectedBase = null);
