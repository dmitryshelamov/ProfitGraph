namespace ProfitGraph.Infrastructure.Notifications;

/// <summary>
///     Telegram's published limits for one process (ADR 0042): at most one message per second to a chat and 25 per second in
///     all. Used by one sender loop at a time.
/// </summary>
internal sealed class TelegramRateLimiter(TimeProvider clock)
{
    private const int ForgetAfterChats = 1000;
    private static readonly TimeSpan PerChat = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PerMessage = TimeSpan.FromMilliseconds(40);

    private readonly Dictionary<long, DateTimeOffset> _lastByChat = new Dictionary<long, DateTimeOffset>();
    private DateTimeOffset _last = DateTimeOffset.MinValue;

    /// <summary>
    ///     Waits until a message to <paramref name="chatId" /> may leave, and counts it as sent.
    /// </summary>
    public async Task WaitTurn(long chatId, CancellationToken cancellationToken)
    {
        DateTimeOffset next = _last + PerMessage;

        if (_lastByChat.TryGetValue(chatId, out DateTimeOffset chatLast) && chatLast + PerChat > next)
        {
            next = chatLast + PerChat;
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
            foreach (long idle in _lastByChat.Where(chat => chat.Value + PerChat <= now).Select(chat => chat.Key).ToArray())
            {
                _lastByChat.Remove(idle);
            }
        }
    }
}