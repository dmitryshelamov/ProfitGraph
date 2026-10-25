namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     An open private connection: its text messages until Bybit closes it. Disposing closes it.
/// </summary>
internal interface IBybitPrivateConnection : IAsyncDisposable
{
    IAsyncEnumerable<string> Messages(CancellationToken cancellationToken);
}