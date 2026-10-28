namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     A bot's private connection and the loop that reads it. The loop replaces a lost connection with a restored one, or
///     with none while it waits to connect again (ADR 0044).
/// </summary>
internal sealed class BybitPrivateStream(IBybitPrivateConnection connection)
{
    private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
    private Task _reading = Task.CompletedTask;

    public IBybitPrivateConnection? Connection { get; private set; } = connection;

    public void Start(Func<BybitPrivateStream, CancellationToken, Task> read)
    {
        _reading = read(this, _stopping.Token);
    }

    /// <summary>
    ///     Closes the current connection and keeps <paramref name="next" /> in its place.
    /// </summary>
    public async Task Replace(IBybitPrivateConnection? next)
    {
        IBybitPrivateConnection? previous = Connection;
        Connection = next;

        if (previous is not null)
        {
            await previous.DisposeAsync();
        }
    }

    public async Task Stop()
    {
        await _stopping.CancelAsync();
        await _reading;
        await Replace(next: null);
        _stopping.Dispose();
    }
}