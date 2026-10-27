namespace ProfitGraph.Application.Bots.Contracts;

/// <summary>
///     What one bot of a venue holds of its instrument by its last checkpoint (ADR 0031): the base total in the instrument's
///     units, zero for a bot without a checkpoint. A venue module that knows which account each bot trades through adds these
///     up per account.
/// </summary>
public sealed record VenueBotPositionInternal(Guid BotId, string Symbol, decimal BaseTotal);