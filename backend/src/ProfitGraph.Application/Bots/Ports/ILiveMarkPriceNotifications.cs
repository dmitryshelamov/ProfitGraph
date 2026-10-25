namespace ProfitGraph.Application.Bots.Ports;

/// <summary>
///     Signals that the Worker stored a newer display price (S5-19). A signal names the stored price, which readers load.
/// </summary>
public interface ILiveMarkPriceNotifications
{
    IAsyncEnumerable<Guid> Listen(CancellationToken cancellationToken);
}