namespace ProfitGraph.Api.Bots.Contracts;

/// <summary>
///     The owner's position close: whether the bot stops once the position is closed.
/// </summary>
public sealed record RequestPositionCloseRequest(bool StopAfter);