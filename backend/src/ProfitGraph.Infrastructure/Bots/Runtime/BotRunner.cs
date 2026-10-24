using System.Threading.Channels;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ProfitGraph.Application.Bots;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Bots.ReleaseBotLease;
using ProfitGraph.Application.Bots.RenewBotLease;
using ProfitGraph.Application.Bots.ReportBotRuntime;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Infrastructure.Bots.Runtime;

/// <summary>
///     Executes one leased bot: opens its engine, feeds live candles and venue updates through one sequential queue and
///     renews the lease. A stop ends it after the current root; a lost lease ends it without writing; any other failure
///     degrades the bot for an automatic restart. The lease is released when it ends.
/// </summary>
internal sealed class BotRunner(Bot bot, LeaseHolderId holder, long epoch, IServiceScopeFactory scopes, BotRuntimeOptions options, TimeProvider clock, ILogger logger)
{
    private readonly Channel<BotRoot> _queue = Channel.CreateUnbounded<BotRoot>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _producers = new CancellationTokenSource();
    private volatile bool _leaseLost;
    private Exception? _producerFailure;

    public BotId BotId => bot.Id;

    public Task Completion { get; private set; } = Task.CompletedTask;

    public void Start(CancellationToken hostToken)
    {
        Completion = Run(hostToken);
    }

    public void StopAfterCurrentRoot()
    {
        _queue.Writer.TryComplete();
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
            await Report(sender, BotRuntimeReport.Running, hostToken);
            tasks.Add(
                Produce(
                    candles.Subscribe(bot.Series.Symbol, bot.Series.Timeframe, session.Checkpoint?.LastClosedCandle.OpenTime, _producers.Token),
                    candle => new BotRoot(candle, Update: null)));
            tasks.Add(Produce(gateway.Updates(bot.Id, _producers.Token), update => new BotRoot(Candle: null, update)));

            await foreach (BotRoot root in _queue.Reader.ReadAllAsync(hostToken))
            {
                if (root.Candle is { } candle)
                {
                    await session.ProcessCandle(candle, hostToken);
                }
                else if (root.Update is { } update)
                {
                    await session.ProcessExternalUpdate(update, hostToken);
                }
            }

            if (_producerFailure is { } failure)
            {
                throw new InvalidOperationException("A live input of the bot failed.", failure);
            }

            if (!_leaseLost)
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
                await sender.Send(new ReleaseBotLeaseCommand(bot.Id.Value, holder.Value, epoch), CancellationToken.None);
            }

            _producers.Dispose();
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

    private async Task KeepLease(CancellationToken hostToken)
    {
        try
        {
            while (!_producers.IsCancellationRequested)
            {
                await Task.Delay(options.RenewInterval, clock, _producers.Token);
                using IServiceScope scope = scopes.CreateScope();

                if (!await scope.ServiceProvider.GetRequiredService<ISender>()
                        .Send(new RenewBotLeaseCommand(bot.Id.Value, holder.Value, epoch, options.LeaseDuration), hostToken))
                {
                    _leaseLost = true;
                    logger.LogWarning("Bot {BotId} could not renew its lease.", bot.Id.Value);
                    _queue.Writer.TryComplete();

                    return;
                }
            }
        }
        catch (OperationCanceledException) when (_producers.IsCancellationRequested || hostToken.IsCancellationRequested)
        {
            // The runner ends.
        }
    }

    private async Task Report(ISender sender, BotRuntimeReport report, CancellationToken cancellationToken)
    {
        if (!await sender.Send(new ReportBotRuntimeCommand(bot.Id.Value, holder.Value, epoch, report), cancellationToken))
        {
            throw new BotLeaseLostException();
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