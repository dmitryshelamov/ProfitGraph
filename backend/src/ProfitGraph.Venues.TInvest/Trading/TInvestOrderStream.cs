namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     One running order state subscription of a bot, stopped when the runner closes it or reconciles again.
/// </summary>
internal sealed class TInvestOrderStream(CancellationTokenSource stop, Task reading)
{
    public async Task Stop()
    {
        await stop.CancelAsync();

        try
        {
            await reading;
        }
        finally
        {
            stop.Dispose();
        }
    }
}