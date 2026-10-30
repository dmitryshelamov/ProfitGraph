using ProfitGraph.Domain.Bots.PositionCloses;

namespace ProfitGraph.Application.Bots;

/// <summary>
///     How a root moved the owner's position close (ADR 0064): it started with <see cref="Started" /> orders asked to cancel, its
///     sell order was submitted, or it ended closed or failed with a reason code. Several steps can happen in one root.
/// </summary>
public sealed record BotRootPositionClose(BotPositionCloseId CloseId, int? Started = null, long? SellOrderId = null, bool Closed = false, string? Failure = null);