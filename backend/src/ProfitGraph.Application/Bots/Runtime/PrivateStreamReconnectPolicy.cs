namespace ProfitGraph.Application.Bots.Runtime;

/// <summary>
///     How a venue keeps a running bot's private order stream after it is lost (ADR 0044): it connects again after each of
///     <see cref="Delays" /> in turn, then after the last one each time, and ends the bot's updates with the loss once the
///     stream has stayed unrestored for <see cref="Budget" />. A restored stream lost again before <see cref="StableAfter" />
///     continues the same outage, so a connection that keeps dropping is bounded as well.
/// </summary>
public sealed class PrivateStreamReconnectPolicy
{
    public IReadOnlyList<TimeSpan> Delays { get; init; } =
        [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    public TimeSpan Budget { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan StableAfter { get; init; } = TimeSpan.FromMinutes(1);
}