using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using ProfitGraph.Venues.Bybit.MarketData;

namespace ProfitGraph.Venues.Bybit.LiveData;

/// <summary>
///     The public WebSocket of one Bybit market (ADR 0061), Spot unless another is given.
/// </summary>
internal sealed class BybitPublicWebSocketTransport(BybitOptions options, BybitMarket? market = null) : IBybitPublicStreamTransport
{
    private const int TopicsPerSubscribe = 10;

    private readonly BybitMarket _market = market ?? BybitMarket.Spot;

    public IAsyncEnumerable<string> Connect(string topic, CancellationToken cancellationToken)
    {
        return Connect([topic], cancellationToken);
    }

    public async IAsyncEnumerable<string> Connect(IReadOnlyList<string> topics, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = options.StreamKeepAliveInterval;
        await socket.ConnectAsync(new Uri(_market.PublicStreamAddress(options)), cancellationToken);

        foreach (string subscribe in SubscribeMessages(topics))
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(subscribe), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }

        var buffer = new byte[8192];

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var message = new MemoryStream();
            WebSocketReceiveResult received;

            do
            {
                received = await socket.ReceiveAsync(buffer, cancellationToken);

                if (received.MessageType == WebSocketMessageType.Close)
                {
                    yield break;
                }

                if (received.MessageType != WebSocketMessageType.Text || message.Length + received.Count > options.MaxStreamMessageBytes)
                {
                    throw new InvalidDataException("Invalid public Spot stream message.");
                }

                message.Write(buffer, offset: 0, received.Count);
            }
            while (!received.EndOfMessage);

            yield return Encoding.UTF8.GetString(message.GetBuffer(), index: 0, (int)message.Length);
        }
    }

    /// <summary>
    ///     The subscribe requests of the topics, at most 10 topics each.
    /// </summary>
    internal static IReadOnlyList<string> SubscribeMessages(IReadOnlyList<string> topics)
    {
        return
        [
            .. topics.Chunk(TopicsPerSubscribe)
                .Select(batch => JsonSerializer.Serialize(new Dictionary<string, object> { ["op"] = "subscribe", ["args"] = batch }))
        ];
    }
}