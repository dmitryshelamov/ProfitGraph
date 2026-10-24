namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Automatic restarts after runtime failures wait 30 s, 1, 2, 5 and 10 minutes; after five failed restarts the bot stays
///     degraded until the owner starts it again (Profit Fox Designer ADR 0054).
/// </summary>
public static class BotRestartSchedule
{
    public const int MaxAttempts = 5;

    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10)
    ];

    /// <summary>
    ///     When the next restart may begin after <paramref name="attempts" /> restarts, or null once they are exhausted.
    /// </summary>
    public static DateTimeOffset? NextRestartAt(int attempts, DateTimeOffset now)
    {
        return attempts < Delays.Length ? now + Delays[attempts] : null;
    }
}