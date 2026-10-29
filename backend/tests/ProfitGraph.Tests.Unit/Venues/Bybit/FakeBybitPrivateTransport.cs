using System.Threading.Channels;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Venues.Bybit;
using ProfitGraph.Venues.Bybit.Trading;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

/// <summary>
///     A private stream in the test process: it records each connection and lets a test push messages or drop it.
/// </summary>
internal sealed class FakeBybitPrivateTransport : IBybitPrivateStreamTransport
{
    private FakeBybitPrivateConnection? _current;

    public List<(string ApiKey, long ExpiresAt)> Connects { get; } = [];

    /// <summary>
    ///     The market each connection subscribed to.
    /// </summary>
    public List<BybitMarket> Markets { get; } = [];

    /// <summary>
    ///     Refuses every following connection.
    /// </summary>
    public bool Refuse { get; set; }

    public bool CurrentIsDisposed => _current?.IsDisposed ?? false;

    public Task<IBybitPrivateConnection> Connect(ExchangeCredentialSecretsInternal credential, long expiresAt, BybitMarket market, CancellationToken cancellationToken)
    {
        if (Refuse)
        {
            throw new InvalidOperationException("Bybit refused the private stream auth.");
        }

        Connects.Add((credential.ApiKey, expiresAt));
        Markets.Add(market);
        _current = new FakeBybitPrivateConnection(Channel.CreateUnbounded<string>());

        return Task.FromResult<IBybitPrivateConnection>(_current);
    }

    public void Push(string message)
    {
        _current!.Messages.Writer.TryWrite(message);
    }

    public void Drop()
    {
        _current!.Messages.Writer.TryComplete();
    }
}