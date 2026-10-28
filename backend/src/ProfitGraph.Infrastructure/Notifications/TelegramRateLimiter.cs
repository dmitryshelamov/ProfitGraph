namespace ProfitGraph.Infrastructure.Notifications;

/// <summary>
///     Telegram's published limits for one process (ADR 0042): <see cref="TelegramOptions.PerChatInterval" /> between messages
///     to a chat and <see cref="TelegramOptions.PerMessageInterval" /> between any two. Used by one sender loop at a time.
/// </summary>
internal sealed class TelegramRateLimiter(TelegramOptions options, TimeProvider clock)
{
    private const int ForgetAfterChats = 1000;

    private readonly Dictionary<long, DateTimeOffset> _lastByChat = new Dictionary<long, DateTimeOffset>();
    private DateTimeOffset _last = DateTimeOffset.MinValue;

    /// <summary>
    ///     Waits until a message to <paramref name="chatId" /> may leave, and counts it as sent.
    /// </summary>
    public async Task WaitTurn(long chatId, CancellationToken cancellationToken)
    {
        DateTimeOffset next = _last + options.PerMessageInterval;

        if (_lastByChat.TryGetValue(chatId, out DateTimeOffset chatLast) && chatLast + options.PerChatInterval > next)
        {
            next = chatLast + options.PerChatInterval;
        }

        TimeSpan wait = next - clock.GetUtcNow();

        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, clock, cancellationToken);
        }

        DateTimeOffset now = clock.GetUtcNow();
        _last = now;
        _lastByChat[chatId] = now;

        if (_lastByChat.Count > ForgetAfterChats)
        {
            foreach (long idle in _lastByChat.Where(chat => chat.Value + options.PerChatInterval <= now).Select(chat => chat.Key).ToArray())
            {
                _lastByChat.Remove(idle);
            }
        }
    }
}