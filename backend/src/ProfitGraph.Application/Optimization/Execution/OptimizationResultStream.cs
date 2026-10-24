using System.Threading.Channels;

namespace ProfitGraph.Application.Optimization.Execution;

/// <summary>
///     Keeps result delivery and timed persistence on the same serial consumer.
/// </summary>
internal static class OptimizationResultStream
{
    internal static async IAsyncEnumerable<T> Read<T>(ChannelReader<T> reader, Func<ValueTask>? idleFlush, TimeSpan interval, TimeProvider clock)
    {
        if (idleFlush is null)
        {
            await foreach (T result in reader.ReadAllAsync())
            {
                yield return result;
            }

            yield break;
        }

        using var waiting = new CancellationTokenSource();
        using var timer = new PeriodicTimer(interval, clock);
        Task<bool> readable = reader.WaitToReadAsync(waiting.Token).AsTask();
        Task<bool> tick = timer.WaitForNextTickAsync().AsTask();

        try
        {
            while (true)
            {
                Task completed = await Task.WhenAny(readable, tick);

                if (completed == tick)
                {
                    if (!await tick)
                    {
                        yield break;
                    }

                    await idleFlush();
                    tick = timer.WaitForNextTickAsync().AsTask();

                    continue;
                }

                if (!await readable)
                {
                    yield break;
                }

                while (reader.TryRead(out T? result))
                {
                    yield return result;
                }

                readable = reader.WaitToReadAsync(waiting.Token).AsTask();
            }
        }
        finally
        {
            await waiting.CancelAsync();

            try
            {
                await readable;
            }
            catch (Exception)
            {
                // Observe a simultaneous producer failure without replacing the receiver's original exception.
            }
        }
    }
}