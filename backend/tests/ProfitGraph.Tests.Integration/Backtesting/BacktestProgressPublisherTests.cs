using System.Threading.Channels;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using ProfitGraph.Api.Backtesting.Contracts;
using ProfitGraph.Api.Backtesting.Realtime;
using ProfitGraph.Application.Backtesting.Contracts;
using ProfitGraph.Application.Backtesting.Ports;
using ProfitGraph.Domain.Backtesting;

namespace ProfitGraph.Tests.Integration.Backtesting;

/// <summary>
///     The publisher against a scripted reader, so the Worker's commits land exactly between its reads.
/// </summary>
public sealed class BacktestProgressPublisherTests
{
    [Test]
    public async Task AResultStoredWhileObservationsAreSent_FinishesTheRunAfterTheLastObservation()
    {
        var runId = new BacktestRunId(Guid.NewGuid());
        var subscriptions = new BacktestProgressSubscriptions();
        subscriptions.Watch(runId.Value, lastSequence: 0);
        List<BacktestObservationInternal> committed = [Observation(sequence: 1), Observation(sequence: 2)];
        string? outcome = null;
        var reader = Substitute.For<IBacktestReader>();
        reader.State(runId, Arg.Any<CancellationToken>())
            .Returns(_ => new BacktestRunStateInternal(
                "Running",
                outcome,
                committed[^1].Sequence,
                LedgerCount: 0,
                committed.Count,
                DiagnosticCount: 0,
                ChartBucketSize: null));
        reader.Observations(runId, Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                List<BacktestObservationInternal> page = [.. committed.Where(observation => observation.Sequence > call.ArgAt<long>(1))];

                // The Worker commits its last batch and then the result after the first read, before the publisher looks again.
                if (outcome is null)
                {
                    committed.Add(Observation(sequence: 3));
                    outcome = "completed";
                }

                return new BacktestPageInternal<BacktestObservationInternal>(page, Next: null);
            });
        var sent = new List<object?>();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var group = Substitute.For<IClientProxy>();
        group.SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call =>
            {
                sent.Add(call.ArgAt<object?[]>(1)[0]);

                if (call.ArgAt<string>(0) == "BacktestFinished")
                {
                    finished.TrySetResult();
                }
            });
        var hub = Substitute.For<IHubContext<BacktestHub>>();
        hub.Clients.Group(BacktestProgressSubscriptions.Group(runId.Value)).Returns(group);

        // One signal per commit: the first batch, then the last batch and the result.
        var signals = Channel.CreateUnbounded<BacktestRunId>();
        signals.Writer.TryWrite(runId);
        signals.Writer.TryWrite(runId);
        var notifications = Substitute.For<IBacktestProgressNotifications>();
        notifications.Listen(Arg.Any<CancellationToken>()).Returns(call => signals.Reader.ReadAllAsync(call.Arg<CancellationToken>()));
        await using ServiceProvider services = new ServiceCollection().AddSingleton(reader).BuildServiceProvider();
        using var publisher = new BacktestProgressPublisher(
            notifications,
            subscriptions,
            hub,
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<BacktestProgressPublisher>.Instance);

        await publisher.StartAsync(CancellationToken.None);
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await publisher.StopAsync(CancellationToken.None);

        sent.OfType<BacktestProgressResponse>().Select(message => message.Observation.Sequence).ShouldBe([1, 2, 3]);
        sent[^1].ShouldBe(new BacktestFinishedResponse(runId.Value, "completed"));
    }

    private static BacktestObservationInternal Observation(long sequence)
    {
        return new BacktestObservationInternal(
            sequence,
            DateTimeOffset.UnixEpoch.AddHours(sequence),
            (int)sequence,
            "Replay",
            MarketPrice: 100m,
            Open: 100m,
            High: 100m,
            Low: 100m,
            Close: 100m,
            Volume: 1m,
            Equity: 1000m,
            UnrealizedPnl: 0m,
            TotalPnl: 0m,
            RealizedPnl: 0m,
            QuoteTotal: 1000m,
            QuoteReserved: 0m,
            BaseTotal: 0m,
            BaseReserved: 0m,
            CostBasis: 0m,
            FeesBase: 0m,
            FeesQuote: 0m,
            LastBuyPrice: null,
            ActiveOrders: 0,
            [],
            []);
    }
}