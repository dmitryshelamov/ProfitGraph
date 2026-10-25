namespace ProfitGraph.Infrastructure.Notifications;

/// <summary>
///     Section <c>Telegram</c> (ADR 0020). Every committed setting keeps <see cref="Enabled" /> false; the bot token
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
            PollTimeout >= TimeSpan.Zero && RetryDelay > TimeSpan.Zero);
    }
}