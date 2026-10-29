using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.Ports;

public interface IScanBacktestReader
{
    /// <summary>
    ///     The owner's latest backtests on the venue, newest first, without their signals.
    /// </summary>
    Task<IReadOnlyList<ScanBacktestListItemInternal>> List(Guid ownerId, string venue, int limit, CancellationToken cancellationToken);
}