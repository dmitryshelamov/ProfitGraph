using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;

using ProfitGraph.Venues.Bybit.MarketData;

namespace ProfitGraph.Venues.Bybit.LiveData;

internal sealed class BybitPublicWebSocketTransport(BybitOptions options) : IBybitPublicStreamTransport
{
    public async IAsyncEnumerable<string> Connect(string topic, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = options.StreamKeepAliveInterval;
        await socket.ConnectAsync(new Uri(options.PublicSpotStreamAddress), cancellationToken);
        byte[] subscribe = Encoding.UTF8.GetBytes($"{{\"op\":\"subscribe\",\"args\":[\"{topic}\"]}}");
        await socket.SendAsync(subscribe, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
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
}