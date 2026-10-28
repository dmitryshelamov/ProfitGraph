namespace ProfitGraph.Domain.Bots.OrderIntents;

/// <summary>
///     A failed delivery is retried after 1, 2, 5, 15 and 30 seconds and then every 60 seconds; the tenth failure fails
///     the intent (Profit Fox Designer ADR 0054).
/// </summary>
public static class OrderIntentRetrySchedule
{
    public const int MaxAttempts = 10;

    /// <summary>
    ///     How long a claim may stay unfinished before another dispatcher may claim the intent again.
    /// </summary>
    public static readonly TimeSpan StaleClaimAfter = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     How long a submit waits when the venue balance cannot be read; this does not count as a failure.
    /// </summary>
    public static readonly TimeSpan TransientRetryDelay = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30)
    ];

    private static readonly TimeSpan LastDelay = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     The delay after the failure that follows <paramref name="previousAttempts" /> failed attempts.
    /// </summary>
    public static TimeSpan DelayAfter(int previousAttempts)
    {
        return previousAttempts < Delays.Length ? Delays[previousAttempts] : LastDelay;
    }
}