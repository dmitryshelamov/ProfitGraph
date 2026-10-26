using System.Runtime.CompilerServices;
using System.Threading.Channels;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ProfitGraph.Application.Bots;
using ProfitGraph.Application.Bots.OpenSubmitGate;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Bots.ReleaseBotLease;
using ProfitGraph.Application.Bots.RenewBotLease;
using ProfitGraph.Application.Bots.ReportBotRuntime;
using ProfitGraph.Application.Bots.ResolveManualOrder;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Infrastructure.Bots.Runtime;

/// <summary>
///     Executes one leased bot: opens its engine, reconciles it with the venue, opens its submit gate, feeds live candles
///     and venue updates through one sequential queue and renews the lease. A venue that cannot answer the reconciliation
///     marks the bot <c>ExchangeUnavailable</c> and is asked again after 5, 15, 30 and 60 s; a safety reason from the venue
///     degrades the bot. A stop ends it after the current root; a lost lease ends it without writing; any other failure
///     degrades the bot for an automatic restart. The lease is released when it ends.
/// </summary>
internal sealed class BotRunner(Bot bot, LeaseHolderId holder, long epoch, IServiceScopeFactory scopes, BotRuntimeOptions options, TimeProvider clock, ILogger logger)
{
    private readonly Channel<BotRoot> _queue = Channel.CreateUnbounded<BotRoot>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _producers = new CancellationTokenSource();
    private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
    private volatile bool _leaseLost;
    private volatile bool _preserveStatus;
    private Exception? _producerFailure;

    public BotId BotId => bot.Id;

    public Task Completion { get; private set; } = Task.CompletedTask;

    public void Start(CancellationToken hostToken)
    {
        Completion = Run(hostToken);
    }

    /// <summary>
    ///     Ends the runner after its current root. With <paramref name="preserveStatus" /> it leaves the bot's degraded
    ///     status as it is instead of reporting it stopped.
    /// </summary>
    public void StopAfterCurrentRoot(bool preserveStatus = false)
    {
        if (preserveStatus)
        {
            _preserveStatus = true;
        }

        _queue.Writer.TryComplete();
        _stopping.Cancel();
    }

    private async Task Run(CancellationToken hostToken)
    {
        using IServiceScope scope = scopes.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var tasks = new List<Task>();

        try
        {
            IOrderGateway gateway = scope.ServiceProvider.GetRequiredService<VenuePorts<IOrderGateway>>().Find(bot.Series.Venue)
                ?? throw new InvalidOperationException("The venue has no order gateway.");
            ILiveCandleSource candles = scope.ServiceProvider.GetRequiredService<VenuePorts<ILiveCandleSource>>().Find(bot.Series.Venue)
                ?? throw new InvalidOperationException("The venue has no live candles.");
            await Report(sender, BotRuntimeReport.Starting, hostToken);
            tasks.Add(KeepLease(hostToken));
            BotEngineSession session = await scope.ServiceProvider.GetRequiredService<BotEngineSessionFactory>().Open(bot, holder, epoch, gateway, hostToken);
            await scope.ServiceProvider.GetRequiredService<BotGraphFollower>().ApplySettings(bot, holder, epoch, session, hostToken);
            BotReconcileResult? reconciled = await Reconcile(scope.ServiceProvider.GetRequiredService<BotReconciler>(), session, gateway, sender, hostToken);

            if (reconciled is { Degradation: not null })
            {
                _preserveStatus = true;
            }
            else if (reconciled is not null)
            {
                await Trade(scope, session, gateway, candles, sender, tasks, hostToken);
            }

            if (_producerFailure is OrderGatewaySafetyException safety)
            {
                await Report(sender, BotRuntimeReport.Degraded, hostToken, safety.Reason);
            }
            else if (_producerFailure is { } failure)
            {
                throw new InvalidOperationException("A live input of the bot failed.", failure);
            }
            else if (!_leaseLost && !_preserveStatus)
            {
                await Report(sender, BotRuntimeReport.Stopped, hostToken);
            }
        }
        catch (BotLeaseLostException)
        {
            _leaseLost = true;
            logger.LogWarning("Bot {BotId} lost its lease.", bot.Id.Value);
        }
        catch (OperationCanceledException) when (hostToken.IsCancellationRequested || _leaseLost)
        {
            // Shutdown and a lost lease discard queued roots after the current one.
        }
        catch (Exception exception)
        {
            logger.LogError("Bot {BotId} failed with {ExceptionType}.", bot.Id.Value, exception.GetType().Name);
            await TryReport(sender, BotRuntimeReport.Failed);
        }
        finally
        {
            await _producers.CancelAsync();
            _queue.Writer.TryComplete();
            await Task.WhenAll(tasks);

            if (!_leaseLost)
            {
                await TryRelease(sender);
            }

            _producers.Dispose();
        }
    }

    /// <summary>
    ///     Opens the submit gate and processes live candles and venue updates until the runner ends. Before each root the
    ///     engine follows the strategy's latest saved version (ADR 0019).
    /// </summary>
    private async Task Trade(
        IServiceScope scope,
        BotEngineSession session,
        IOrderGateway gateway,
        ILiveCandleSource candles,
        ISender sender,
        List<Task> tasks,
        CancellationToken hostToken)
    {
        await Report(sender, BotRuntimeReport.Running, hostToken);

        if (!await sender.Send(new OpenSubmitGateCommand(bot.Id.Value, holder.Value, epoch), hostToken))
        {
            throw new BotLeaseLostException();
        }

        // A venue without anonymous market data reads live candles with the bot's credential (ADR 0023, TC6).
        using (MarketDataAccess.Use(await scope.ServiceProvider.GetRequiredService<BotMarketDataAccess>().Read(bot, hostToken)))
        {
            tasks.Add(
                Produce(
                    candles.Subscribe(bot.Series.Symbol, bot.Series.Timeframe, session.Checkpoint?.LastClosedCandle.OpenTime, _producers.Token),
                    candle => new BotRoot(candle, Update: null)));
        }

        tasks.Add(Produce(gateway.Updates(bot.Id, _producers.Token), update => new BotRoot(Candle: null, update)));
        tasks.Add(Produce(scope.ServiceProvider.GetRequiredService<BotUpdateBus>().Read(bot.Id, _producers.Token), update => new BotRoot(Candle: null, update)));
        tasks.Add(Produce(PendingManualOrders(_producers.Token), id => new BotRoot(Candle: null, Update: null, id)));
        tasks.Add(Produce(NewStrategyVersions(session, _producers.Token), _ => new BotRoot(Candle: null, Update: null, GraphChange: true)));

        var follower = scope.ServiceProvider.GetRequiredService<BotGraphFollower>();

        await foreach (BotRoot root in _queue.Reader.ReadAllAsync(hostToken))
        {
            await follower.Follow(bot, holder, epoch, session, hostToken);

            if (root.Candle is { } candle)
            {
                await session.ProcessCandle(candle, hostToken);
            }
            else if (root.Update is { } update)
            {
                await session.ProcessExternalUpdate(update, hostToken);
            }
            else if (root.ManualOrder is { } manualOrder)
            {
                await ProcessManualOrder(scope, sender, session, manualOrder, hostToken);
            }
        }
    }

    /// <summary>
    ///     A pending request is submitted to the engine with its resolution in the root's commit; one that waited past its
    ///     lifetime is rejected as expired without reaching the engine.
    /// </summary>
    private async Task ProcessManualOrder(IServiceScope scope, ISender sender, BotEngineSession session, ManualOrderRequestId id, CancellationToken hostToken)
    {
        ManualOrderRequest? request = await scope.ServiceProvider.GetRequiredService<IManualOrderRequestRepository>().Get(id, hostToken);

        if (request is not { Status: ManualOrderStatus.Pending })
        {
            return;
        }

        if (request.IsExpired(clock.GetUtcNow()))
        {
            if (!await sender.Send(new ResolveManualOrderCommand(bot.Id.Value, holder.Value, epoch, id.Value, "expired"), hostToken))
            {
                throw new BotLeaseLostException();
            }

            return;
        }

        await session.ProcessManualOrder(request, hostToken);
    }

    /// <summary>
    ///     Each newer saved version of the bot's strategy once, as soon as it is found: its root only makes the runner follow it
    ///     (ADR 0019), so a running bot with nothing else to do does not wait for the next candle.
    /// </summary>
    private async IAsyncEnumerable<StrategyVersionId> NewStrategyVersions(BotEngineSession session, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        StrategyVersionId? last = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(options.GraphCheckInterval, clock, cancellationToken);
            using IServiceScope scope = scopes.CreateScope();
            StrategyVersionId? candidate = await scope.ServiceProvider.GetRequiredService<BotGraphFollower>().Candidate(bot, session.Version, cancellationToken);

            if (candidate is { } version && version != last)
            {
                last = version;

                yield return version;
            }
        }
    }

    /// <summary>
    ///     The bot's pending manual order each time a new one appears.
    /// </summary>
    private async IAsyncEnumerable<ManualOrderRequestId> PendingManualOrders([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ManualOrderRequestId? last = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(options.ManualOrderPollInterval, clock, cancellationToken);
            using IServiceScope scope = scopes.CreateScope();
            ManualOrderRequest? pending = await scope.ServiceProvider.GetRequiredService<IManualOrderRequestRepository>().GetPending(bot.Id, cancellationToken);

            if (pending is not null && pending.Id != last)
            {
                last = pending.Id;

                yield return pending.Id;
            }
        }
    }

    /// <summary>
    ///     Reconciles until the venue answers. Returns null when the runner is stopped first.
    /// </summary>
    private async Task<BotReconcileResult?> Reconcile(
        BotReconciler reconciler,
        BotEngineSession session,
        IOrderGateway gateway,
        ISender sender,
        CancellationToken hostToken)
    {
        int[] factors = [1, 3, 6, 12];

        for (var attempt = 0;; attempt++)
        {
            BotReconcileResult result = await reconciler.Reconcile(bot, holder, epoch, session, gateway, hostToken);

            if (result.VenueAnswered)
            {
                return result;
            }

            logger.LogWarning("The venue could not reconcile bot {BotId}; it is asked again.", bot.Id.Value);
            await Report(sender, BotRuntimeReport.ExchangeUnavailable, hostToken);

            try
            {
                await Task.Delay(options.ReconcileRetryDelay * factors[Math.Min(attempt, factors.Length - 1)], clock, _stopping.Token);
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                return null;
            }
        }
    }

    private async Task Produce<T>(IAsyncEnumerable<T> items, Func<T, BotRoot> root)
    {
        try
        {
            await foreach (T item in items)
            {
                await _queue.Writer.WriteAsync(root(item), _producers.Token);
            }
        }
        catch (OperationCanceledException) when (_producers.IsCancellationRequested)
        {
            // The runner ends.
        }
        catch (ChannelClosedException)
        {
            // The runner stopped reading.
        }
        catch (Exception exception)
        {
            _producerFailure ??= exception;
            _queue.Writer.TryComplete();
        }
    }

    /// <summary>
    ///     Renews the lease every interval. A failed renewal is retried while the lease still lasts; a refused or expired
    ///     one means another runtime may take the bot, so the runner stops.
    /// </summary>
    private async Task KeepLease(CancellationToken hostToken)
    {
        DateTimeOffset renewedAt = clock.GetUtcNow();

        try
        {
            while (!_producers.IsCancellationRequested)
            {
                await Task.Delay(options.RenewInterval, clock, _producers.Token);
                bool renewed;

                try
                {
                    using IServiceScope scope = scopes.CreateScope();
                    renewed = await scope.ServiceProvider.GetRequiredService<ISender>()
                        .Send(new RenewBotLeaseCommand(bot.Id.Value, holder.Value, epoch, options.LeaseDuration), hostToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException && clock.GetUtcNow() - renewedAt < options.LeaseDuration)
                {
                    logger.LogWarning("Renewing the lease of bot {BotId} failed with {ExceptionType}; it is retried.", bot.Id.Value, exception.GetType().Name);

                    continue;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    renewed = false;
                }

                if (!renewed)
                {
                    _leaseLost = true;
                    logger.LogWarning("Bot {BotId} could not renew its lease.", bot.Id.Value);
                    _queue.Writer.TryComplete();
                    await _stopping.CancelAsync();

                    return;
                }

                renewedAt = clock.GetUtcNow();
            }
        }
        catch (OperationCanceledException) when (_producers.IsCancellationRequested || hostToken.IsCancellationRequested)
        {
            // The runner ends.
        }
    }

    private async Task Report(ISender sender, BotRuntimeReport report, CancellationToken cancellationToken, BotStatusReason? reason = null)
    {
        if (!await sender.Send(new ReportBotRuntimeCommand(bot.Id.Value, holder.Value, epoch, report, reason), cancellationToken))
        {
            throw new BotLeaseLostException();
        }
    }

    /// <summary>
    ///     A lease that cannot be released expires by itself; the runner ends either way.
    /// </summary>
    private async Task TryRelease(ISender sender)
    {
        try
        {
            await sender.Send(new ReleaseBotLeaseCommand(bot.Id.Value, holder.Value, epoch), CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError("Releasing the lease of bot {BotId} failed with {ExceptionType}.", bot.Id.Value, exception.GetType().Name);
        }
    }

    private async Task TryReport(ISender sender, BotRuntimeReport report)
    {
        try
        {
            await sender.Send(new ReportBotRuntimeCommand(bot.Id.Value, holder.Value, epoch, report), CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError("Reporting bot {BotId} failed with {ExceptionType}.", bot.Id.Value, exception.GetType().Name);
        }
    }
}