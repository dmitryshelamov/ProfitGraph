using MediatR;

using ProfitGraph.Application.Jobs;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Converters;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Application.Scanner.RunScanBacktest;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Jobs;
using ProfitGraph.Domain.Scanner;

namespace ProfitGraph.Application.Scanner.StartScanBacktest;

internal sealed class StartScanBacktestCommandHandler(
    IEnumerable<IMarketScanSource> sources,
    IScanBacktestRepository backtests,
    BackgroundJobQueue queue,
    TimeProvider clock) : IRequestHandler<StartScanBacktestCommand, ScanBacktestInternal>
{
    /// <summary>
    ///     How far back a backtest looks: 30 days (owner).
    /// </summary>
    public static readonly TimeSpan Period = TimeSpan.FromDays(30);

    public async Task<ScanBacktestInternal> Handle(StartScanBacktestCommand command, CancellationToken cancellationToken)
    {
        IMarketScanSource source = sources.Find(command.Venue);
        var owner = new UserId(command.UserId);
        DateTimeOffset now = clock.GetUtcNow();
        var to = new DateTimeOffset(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
        var filter = new ScanFilter(command.Definition.ToDomainConditions(), command.Definition.HideStale);
        var jobId = BackgroundJobId.New();
        ScanBacktest backtest = ScanBacktest.Start(owner, source.Venue, new ScanPresetName(command.Definition.Name), filter, to - Period, to, jobId, now);

        await backtests.Add(backtest, cancellationToken);
        await queue.Enqueue(jobId, owner, new RunScanBacktestCommand(backtest.Id.Value), cancellationToken);

        return backtest.ToInternal();
    }
}