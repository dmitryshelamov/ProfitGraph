using System.Threading.Channels;

using ProfitGraph.Application.Optimization.Execution;

namespace ProfitGraph.Tests.Unit.Application.Optimization;

public sealed class OptimizationResultStreamTests
{
    [Test]
    public async Task BufferedResultsAreDeliveredBeforeProducerFailure()
    {
        var channel = Channel.CreateUnbounded<int>();
        await channel.Writer.WriteAsync(item: 1);
        var failure = new InvalidOperationException("producer failed");
        channel.Writer.TryComplete(failure);
        var delivered = new List<int>();
        var caught = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (int result in OptimizationResultStream.Read(channel.Reader, () => ValueTask.CompletedTask, TimeSpan.FromSeconds(1), TimeProvider.System))
            {
                delivered.Add(result);
            }
        });
        caught.ShouldBeSameAs(failure);
        delivered.ShouldBe(new[] { 1 });
    }

    [Test]
    public async Task IdleFlushFailureIsPreservedWhenProducerFailsAtTheSameTime()
    {
        var channel = Channel.CreateUnbounded<int>();
        await channel.Writer.WriteAsync(item: 1);
        var failure = new InvalidOperationException("persistence failed");
        var delivered = new List<int>();
        var caught = await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            await foreach (int trial in OptimizationResultStream.Read(channel.Reader, Flush, TimeSpan.FromMilliseconds(20), TimeProvider.System))
            {
                delivered.Add(trial);
            }
        });
        caught.ShouldBeSameAs(failure);
        delivered.ShouldBe(new[] { 1 });

        ValueTask Flush()
        {
            channel.Writer.TryComplete(new InvalidOperationException("producer failed"));

            throw failure;
        }
    }

    [Test]
    public async Task IncomingResultCannotOverlapAnAwaitedIdleFlush()
    {
        var channel = Channel.CreateUnbounded<int>();
        var flushing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = false;
        Task consumer = Consume();
        await channel.Writer.WriteAsync(item: 1);

        try
        {
            await flushing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await channel.Writer.WriteAsync(item: 2);
            await Should.ThrowAsync<TimeoutException>(() => second.Task.WaitAsync(TimeSpan.FromMilliseconds(100)));
        }
        finally
        {
            release.TrySetResult();
            channel.Writer.TryComplete();
            await consumer.WaitAsync(TimeSpan.FromSeconds(5));
        }

        second.Task.IsCompletedSuccessfully.ShouldBeTrue();

        async Task Consume()
        {
            await foreach (int trial in OptimizationResultStream.Read(channel.Reader, Flush, TimeSpan.FromMilliseconds(20), TimeProvider.System))
            {
                pending = true;

                if (trial == 2)
                {
                    second.TrySetResult();
                }
            }
        }

        async ValueTask Flush()
        {
            if (pending)
            {
                flushing.TrySetResult();
                await release.Task;
                pending = false;
            }
        }
    }

    [Test]
    public async Task CompletedTrialIsCommittedWhileFollowingTrialRemainsBlocked()
    {
        var channel = Channel.CreateUnbounded<int>();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var committed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new List<int>();
        var persisted = new List<int>();
        Task consumer = Consume();
        await channel.Writer.WriteAsync(item: 1);

        try
        {
            await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            int completed = await committed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            completed.ShouldBe(1);
            consumer.IsCompleted.ShouldBeFalse();
            persisted.ShouldBe(new[] { 1 });
            await channel.Writer.WriteAsync(item: 2);
        }
        finally
        {
            channel.Writer.TryComplete();
            await consumer.WaitAsync(TimeSpan.FromSeconds(5));
        }

        async Task Consume()
        {
            await foreach (int trial in OptimizationResultStream.Read(channel.Reader, Flush, TimeSpan.FromSeconds(1), TimeProvider.System))
            {
                pending.Add(trial);
                received.TrySetResult();
            }
        }

        ValueTask Flush()
        {
            if (pending.Count > 0)
            {
                persisted.AddRange(pending);
                pending.Clear();
                committed.TrySetResult(persisted.Count);
            }

            return ValueTask.CompletedTask;
        }
    }
}