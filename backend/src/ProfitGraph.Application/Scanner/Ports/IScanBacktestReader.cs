using ProfitGraph.Application.Scanner.Contracts;

namespace ProfitGraph.Application.Scanner.Ports;

public interface IScanBacktestReader
{
    /// <summary>
    ///     The owner's latest backtests on the venue, newest first, without their signals.
    /// </summary>
    Task<IReadOnlyList<ScanBacktestListItemInternal>> List(Guid ownerId, string venue, int limit, CancellationToken cancellationToken);

    /// <summary>
    ///     The odds of the owner's latest completed backtest on the venue that followed its cards (ADR 0071, section 1); null without one.
    /// </summary>
    Task<ScanCardOddsSourceInternal?> LatestCardOdds(Guid ownerId, string venue, CancellationToken cancellationToken);
}