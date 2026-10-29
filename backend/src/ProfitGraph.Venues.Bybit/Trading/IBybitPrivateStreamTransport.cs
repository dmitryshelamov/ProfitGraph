using ProfitGraph.Application.Accounts.Contracts;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     Bybit's private V5 stream. A connection is authenticated with the credential and subscribed to the order and
///     execution topics of one market (Spot, or USDT perpetuals by ADR 0061) before it is returned; it ends when Bybit closes it.
/// </summary>
internal interface IBybitPrivateStreamTransport
{
    Task<IBybitPrivateConnection> Connect(ExchangeCredentialSecretsInternal credential, long expiresAt, BybitMarket market, CancellationToken cancellationToken);
}