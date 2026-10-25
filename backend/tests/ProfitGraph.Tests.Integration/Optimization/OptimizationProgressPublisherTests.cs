using System.Threading.Channels;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using ProfitGraph.Api.Optimization.Contracts;
using ProfitGraph.Api.Optimization.Realtime;
using ProfitGraph.Application.Optimization.Contracts;
using ProfitGraph.Application.Optimization.Ports;
using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Tests.Integration.Optimization;

/// <summary>
///     The publisher against a scripted reader, so the Worker's commits land exactly between its reads.
/// </summary>
public sealed class OptimizationProgressPublisherTests
{
    [Test]
    public async Task AnEndStoredWhileTrialsAreSent_FinishesTheOptimizationAfterTheLastTrial()
    {
        var id = new OptimizationId(Guid.NewGuid());
        var subscriptions = new OptimizationProgressSubscriptions();

        List<object?> sent = await Publish(id, subscriptions, "Completed", "OptimizationFinished");

        sent.OfType<OptimizationProgressResponse>().Select(message => message.Trial.Sequence).ShouldBe([1, 2, 3]);
        sent[^1].ShouldBe(new OptimizationFinishedResponse(id.Value, "Completed"));
        subscriptions.TryGetSent(id.Value, out _).ShouldBeFalse();
    }

    [Test]
    public async Task APauseStoredWhileTrialsAreSent_PausesTheOptimizationAfterTheLastTrial()
    {
        var id = new OptimizationId(Guid.NewGuid());
        var subscriptions = new OptimizationProgressSubscriptions();

        List<object?> sent = await Publish(id, subscriptions, "Paused", "OptimizationPaused");

        sent.OfType<OptimizationProgressResponse>().Select(message => message.Trial.Sequence).ShouldBe([1, 2, 3]);
        sent[^1].ShouldBe(new OptimizationFinishedResponse(id.Value, "Paused"));
        subscriptions.TryGetSent(id.Value, out long last).ShouldBeTrue();
        last.ShouldBe(3);
    }

    /// <summary>
    ///     Publishes two signals while the Worker stores the last trial and then <paramref name="end" /> right after the
    ///     publisher's first trial read, and returns the messages sent up to the first <paramref name="endMessage" />.
    /// </summary>
    private static async Task<List<object?>> Publish(OptimizationId id, OptimizationProgressSubscriptions subscriptions, string end, string endMessage)
    {
        subscriptions.Watch(id.Value, lastSequence: 0);
        List<OptimizationTrialInternal> committed = [Trial(sequence: 1), Trial(sequence: 2)];
        var status = "Running";
        var reader = Substitute.For<IOptimizationReader>();
        reader.Cursor(id, Arg.Any<CancellationToken>()).Returns(_ => new OptimizationCursorInternal(committed[^1].Sequence, status));
        reader.Trials(id, Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                List<OptimizationTrialInternal> page = [.. committed.Where(trial => trial.Sequence > call.ArgAt<long>(1))];

                // The Worker commits its last batch and then the job's end after the first read, before the publisher looks again.
                if (status == "Running")
                {
                    committed.Add(Trial(sequence: 3));
                    status = end;
                }

                return new OptimizationTrialPageInternal(page, Next: null);
            });
        var sent = new List<object?>();
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var group = Substitute.For<IClientProxy>();
        group.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call =>
            {
                if (ended.Task.IsCompleted)
                {
                    return;
                }

                sent.Add(call.ArgAt<object?[]>(1)[0]);

                if (call.ArgAt<string>(0) == endMessage)
                {
                    ended.TrySetResult();
                }
            });
        var hub = Substitute.For<IHubContext<OptimizationHub>>();
        hub.Clients.Group(OptimizationProgressSubscriptions.Group(id.Value)).Returns(group);

        // One signal per commit: the first batch, then the last batch and the end.
        var signals = Channel.CreateUnbounded<OptimizationId>();
        signals.Writer.TryWrite(id);
        signals.Writer.TryWrite(id);
        var notifications = Substitute.For<IOptimizationProgressNotifications>();
        notifications.Listen(Arg.Any<CancellationToken>()).Returns(call => signals.Reader.ReadAllAsync(call.Arg<CancellationToken>()));
        await using ServiceProvider services = new ServiceCollection().AddSingleton(reader).BuildServiceProvider();
        using var publisher = new OptimizationProgressPublisher(
            notifications,
            subscriptions,
            hub,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OptimizationProgressPublisher>.Instance);

        await publisher.StartAsync(CancellationToken.None);
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await publisher.StopAsync(CancellationToken.None);

        return sent;
    }

    private static OptimizationTrialInternal Trial(long sequence)
    {
        return new OptimizationTrialInternal(
            sequence,
            (int)sequence,
            new Dictionary<string, decimal> { ["fast"] = sequence },
            "completed",
            FailureCode: null,
            Objective: 1m,
            ReturnPercent: 1m,
            TotalPnl: 1m,
            MaxDrawdownPercent: 0m,
            Executions: 1,
            ElapsedMs: 1);
    }
}