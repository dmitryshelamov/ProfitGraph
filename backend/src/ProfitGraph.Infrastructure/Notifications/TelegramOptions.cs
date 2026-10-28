namespace ProfitGraph.Infrastructure.Notifications;

/// <summary>
///     Section <c>Telegram</c> (ADR 0020, ADR 0042). Every committed setting keeps <see cref="Enabled" /> false; the bot token
///     comes from private configuration and never reaches a log.
/// </summary>
public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public bool Enabled { get; init; }

    public string BaseAddress { get; init; } = "https://api.telegram.org/";

    public string? BotToken { get; init; }

    /// <summary>
    ///     The bot's username, for the <c>t.me</c> link that starts it with a code.
    /// </summary>
    public string? BotUsername { get; init; }

    /// <summary>
    ///     How long one <c>getUpdates</c> request waits for an update.
    /// </summary>
    public TimeSpan PollTimeout { get; init; } = TimeSpan.FromSeconds(25);

    /// <summary>
    ///     How long the process waits after a failed pass or while another process polls.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     How often the delivery host looks for due notifications while none was due (ADR 0042).
    /// </summary>
    public TimeSpan DeliveryInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     How many due notifications one delivery pass claims.
    /// </summary>
    public int DeliveryBatchSize { get; init; } = 10;

    /// <summary>
    ///     How often finished notifications are pruned.
    /// </summary>
    public TimeSpan PruneInterval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>
    ///     How long the poller lease lasts; the process that holds it reads the bot's updates.
    /// </summary>
    public TimeSpan PollerLeaseDuration { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     The shortest time between two messages to one chat (Telegram's published limit, ADR 0042).
    /// </summary>
    public TimeSpan PerChatInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     The shortest time between two messages of one process: 25 per second (Telegram's published limit, ADR 0042).
    /// </summary>
    public TimeSpan PerMessageInterval { get; init; } = TimeSpan.FromMilliseconds(40);

    /// <summary>
    ///     The <c>t.me</c> link that starts the bot with <paramref name="code" />, when the bot's username is known.
    /// </summary>
    public string? StartLink(string code)
    {
        return BotUsername is { Length: > 0 } username ? $"https://t.me/{username}?start={code}" : null;
    }

    public bool IsValid()
    {
        return !Enabled || (!string.IsNullOrWhiteSpace(BotToken) && Uri.TryCreate(BaseAddress, UriKind.Absolute, out Uri? address) &&
            address.Scheme is "https" or "http" &&
            PollTimeout >= TimeSpan.Zero && RetryDelay > TimeSpan.Zero && DeliveryInterval > TimeSpan.Zero && DeliveryBatchSize > 0 &&
            PruneInterval > TimeSpan.Zero && PollerLeaseDuration > PollTimeout && PerChatInterval >= TimeSpan.Zero &&
            PerMessageInterval >= TimeSpan.Zero);
    }
}