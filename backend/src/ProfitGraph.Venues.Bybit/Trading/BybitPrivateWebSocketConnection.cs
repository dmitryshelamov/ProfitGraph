using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     One private WebSocket. It sends Bybit's application ping every 20 s while messages are read.
/// </summary>
internal sealed class BybitPrivateWebSocketConnection(ClientWebSocket socket) : IBybitPrivateConnection
{
    private const int MaxMessageBytes = 1_048_576;
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(20);

    private readonly SemaphoreSlim _sending = new SemaphoreSlim(initialCount: 1, maxCount: 1);

    public async IAsyncEnumerable<string> Messages([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task pinging = KeepAlive(stopping.Token);

        try
        {
            while (await Receive(cancellationToken) is { } message)
            {
                yield return message;
            }
        }
        finally
        {
            await stopping.CancelAsync();

            try
            {
                await pinging;
            }
            catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
            {
                // The connection ends.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, statusDescription: null, timeout.Token);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
        {
            // The socket is disposed either way.
        }
        finally
        {
            socket.Dispose();
            _sending.Dispose();
        }
    }

    public async Task Send(string message, CancellationToken cancellationToken)
    {
        await _sending.WaitAsync(cancellationToken);

        try
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        finally
        {
            _sending.Release();
        }
    }

    /// <summary>
    ///     Reads until Bybit confirms <paramref name="op" />; a refusal or a closed socket throws.
    /// </summary>
    public async Task Confirm(string op, CancellationToken cancellationToken)
    {
        while (await Receive(cancellationToken) is { } message)
        {
            switch (BybitPrivateStreamMessages.Confirms(message, op))
            {
                case true:
                    return;
                case false:
                    throw new InvalidOperationException($"Bybit refused the private stream {op}.");
            }
        }

        throw new WebSocketException("Bybit closed the private stream.");
    }

    private async Task KeepAlive(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(PingInterval, cancellationToken);
            await Send(BybitPrivateStreamMessages.Ping, cancellationToken);
        }
    }

    private async Task<string?> Receive(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var message = new MemoryStream();
        WebSocketReceiveResult received;

        do
        {
            received = await socket.ReceiveAsync(buffer, cancellationToken);

            if (received.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (received.MessageType != WebSocketMessageType.Text || message.Length + received.Count > MaxMessageBytes)
            {
                throw new InvalidDataException("Invalid Bybit private stream message.");
            }

            message.Write(buffer, offset: 0, received.Count);
        }
        while (!received.EndOfMessage);

        return Encoding.UTF8.GetString(message.GetBuffer(), index: 0, (int)message.Length);
    }
}