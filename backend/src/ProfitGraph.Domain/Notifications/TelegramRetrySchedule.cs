namespace ProfitGraph.Domain.Notifications;

/// <summary>
///     A message Telegram could not take now waits 5 s, 15 s, 1, 5 and then 10 minutes between attempts, or Telegram's
///     <c>retry_after</c> when it is longer (ADR 0042, Profit Fox Designer's retry policy). Attempts are not limited; the age
///     of a notification ends them.
/// </summary>
public static class TelegramRetrySchedule
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10)
    ];

    /// <summary>
    ///     When the next attempt may begin after <paramref name="failedAttempts" /> failed attempts.
    /// </summary>
    public static DateTimeOffset NextAttemptAt(int failedAttempts, TimeSpan? retryAfter, DateTimeOffset now)
    {
        TimeSpan delay = Delays[Math.Clamp(failedAttempts - 1, min: 0, Delays.Length - 1)];

        return now + (retryAfter is { } wait && wait > delay ? wait : delay);
    }
}