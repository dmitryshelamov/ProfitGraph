using Microsoft.Extensions.Logging;

namespace ProfitGraph.Infrastructure.Jobs;

/// <summary>
///     Checks a running job's stop requests on every polling interval and stops its work on a cancellation or pause request
///     (ADR 0011). A failed check is logged and the next interval checks again, so a database error never leaves a running
///     job that can no longer be stopped.
/// </summary>
internal sealed class BackgroundJobStopWatcher(
    Func<CancellationToken, Task<(bool Cancel, bool Pause)>> check,
    TimeSpan interval,
    TimeProvider clock,
    ILogger logger)
{
    /// <summary>
    ///     Watches until a stop request cancels <paramref name="work" /> or <paramref name="stop" /> ends the watch.
    /// </summary>
    public async Task Watch(CancellationTokenSource work, CancellationToken stop)
    {
        try
        {
            using var timer = new PeriodicTimer(interval, clock);

            while (await timer.WaitForNextTickAsync(stop))
            {
                try
                {
                    (bool cancel, bool pause) = await check(stop);

                    if (cancel || pause)
                    {
                        await work.CancelAsync();

                        return;
                    }
                }
                catch (Exception exception) when (!stop.IsCancellationRequested)
                {
                    logger.LogError(exception, "Checking job stop requests failed; they are checked again on the next interval.");
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            // The work finished first.
        }
    }
}