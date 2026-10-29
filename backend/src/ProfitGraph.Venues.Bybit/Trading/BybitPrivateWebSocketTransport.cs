using System.Net.WebSockets;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Venues.Bybit.MarketData;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     Bybit's private WebSocket: it authenticates, subscribes to the market's orders and executions, and returns the connection
///     once Bybit confirmed both within the timeout. A refusal or a timeout throws.
/// </summary>
internal sealed class BybitPrivateWebSocketTransport(BybitOptions options) : IBybitPrivateStreamTransport
{
    public async Task<IBybitPrivateConnection> Connect(
        ExchangeCredentialSecretsInternal credential,
        long expiresAt,
        BybitMarket market,
        CancellationToken cancellationToken)
    {
        var socket = new ClientWebSocket();

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.PrivateStreamConfirmTimeout);
            socket.Options.KeepAliveInterval = options.StreamKeepAliveInterval;
            await socket.ConnectAsync(new Uri(options.PrivateStreamAddress), timeout.Token);
            var connection = new BybitPrivateWebSocketConnection(socket, options);
            await connection.Send(BybitPrivateStreamMessages.Auth(credential.ApiKey, credential.ApiSecret, expiresAt), timeout.Token);
            await connection.Confirm("auth", timeout.Token);
            await connection.Send(market.IsLinear ? BybitPrivateStreamMessages.SubscribeLinear : BybitPrivateStreamMessages.Subscribe, timeout.Token);
            await connection.Confirm("subscribe", timeout.Token);

            return connection;
        }
        catch
        {
            socket.Dispose();

            throw;
        }
    }
}