namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Automatic restarts after runtime failures wait 30 s, then 1 minute each, without a limit (ADR 0050). The restart
///     counter stops at <see cref="MaxCountedAttempts" />; it only shows that a streak is running.
/// </summary>
public static class BotRestartSchedule
{
    public const int MaxCountedAttempts = 5;

    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     When the next restart may begin after <paramref name="attempts" /> restarts.
    /// </summary>
    public static DateTimeOffset NextRestartAt(int attempts, DateTimeOffset now)
    {
        return now + (attempts == 0 ? FirstDelay : RepeatDelay);
    }
}
