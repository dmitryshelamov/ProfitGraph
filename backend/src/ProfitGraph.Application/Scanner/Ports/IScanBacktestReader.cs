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

    /// <summary>
    ///     The owner's latest completed backtests on the venue that stored their cards, newest first (ADR 0071, section 5).
    /// </summary>
    Task<IReadOnlyList<ScanBacktestEvidenceInternal>> Evidence(Guid ownerId, string venue, int limit, CancellationToken cancellationToken);

    /// <summary>
    ///     The backtest's closed card trades by kind and third whose expected R and chance reach the minimums.
    /// </summary>
    Task<IReadOnlyList<ScanCardGroupInternal>> CardGroups(Guid backtestId, decimal minExpectedR, decimal? minChance, CancellationToken cancellationToken);
}