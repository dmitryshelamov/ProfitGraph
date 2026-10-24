namespace ProfitGraph.Venues.Bybit.LiveData;

/// <summary>
///     Text messages of one public Spot topic; the connection is never authenticated. It ends when the venue closes it.
/// </summary>
internal interface IBybitPublicStreamTransport
{
    IAsyncEnumerable<string> Connect(string topic, CancellationToken cancellationToken);
}