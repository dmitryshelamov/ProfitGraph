namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     A bot's open private connection and the loop that reads it.
/// </summary>
internal sealed class BybitPrivateStream(IBybitPrivateConnection connection)
{
    private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
    private Task _reading = Task.CompletedTask;

    public void Start(Func<IBybitPrivateConnection, CancellationToken, Task> read)
    {
        _reading = read(connection, _stopping.Token);
    }

    public async Task Stop()
    {
        await _stopping.CancelAsync();
        await _reading;
        await connection.DisposeAsync();
        _stopping.Dispose();
    }
}