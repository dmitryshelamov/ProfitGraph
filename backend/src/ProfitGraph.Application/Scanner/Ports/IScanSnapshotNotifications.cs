namespace ProfitGraph.Application.Scanner.Ports;

/// <summary>
///     Signals that a scanner snapshot was stored; a broken listener throws and the caller listens again.
/// </summary>
public interface IScanSnapshotNotifications
{
    IAsyncEnumerable<Guid> Listen(CancellationToken cancellationToken);
}