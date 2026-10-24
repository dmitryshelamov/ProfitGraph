using System.Runtime.CompilerServices;
using System.Threading.Channels;

using ProfitGraph.Venues.Bybit.LiveData;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     A public stream the test feeds by hand; each connection reads the messages pushed while it is open.
/// </summary>
internal sealed class FakePublicStreamTransport : IBybitPublicStreamTransport
{
    private Channel<string> _current = Channel.CreateUnbounded<string>();
    private int _connections;

    public int Connections => Volatile.Read(ref _connections);

    public string? LastTopic { get; private set; }

    public async IAsyncEnumerable<string> Connect(string topic, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        LastTopic = topic;
        var channel = Channel.CreateUnbounded<string>();
        Interlocked.Exchange(ref _current, channel);
        Interlocked.Increment(ref _connections);

        await foreach (string message in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return message;
        }
    }

    public void Push(string message)
    {
        Volatile.Read(ref _current).Writer.TryWrite(message);
    }

    public static string Kline(string topic, DateTimeOffset openTime, decimal close, bool confirm = true)
    {
        return $$"""
                 {"topic":"{{topic}}","type":"snapshot","data":[{"start":{{openTime.ToUnixTimeMilliseconds()}},"open":"100","high":"110","low":"90","close":"{{close}}","volume":"5","confirm":{{(confirm ? "true" : "false")}}}]}
                 """;
    }
}