namespace ProfitGraph.Venues.Bybit.LiveData;

/// <summary>
///     Text messages of public Spot topics; the connection is never authenticated. It ends when the venue closes it.
/// </summary>
internal interface IBybitPublicStreamTransport
{
    IAsyncEnumerable<string> Connect(string topic, CancellationToken cancellationToken);

    /// <summary>
    ///     One connection that carries every topic; Bybit takes at most 10 topics per subscribe request, so they go in batches.
    /// </summary>
    IAsyncEnumerable<string> Connect(IReadOnlyList<string> topics, CancellationToken cancellationToken);
}