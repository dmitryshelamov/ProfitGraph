using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Npgsql;

using ProfitGraph.Application.Bots.AcquireBotLease;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Bots.ReleaseBotLease;
using ProfitGraph.Application.Bots.ReportBotRuntime;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Leases;

namespace ProfitGraph.Infrastructure.Bots.Runtime;

/// <summary>
///     The live role's bot loop (ADR 0016): on every polling interval it beats its heartbeat, stops runners of bots that
///     should no longer run and takes the leases of bots that should run. A bot whose venue has no order gateway in this
///     process, because trading is disabled, degrades with <c>NoGateway</c> and is left alone. A bot started again before
///     its trading runner stopped, after a kill had marked it stopped, is reported running again by that runner.
/// </summary>
internal sealed class BotRuntimeHost(
    IServiceScopeFactory scopes,
    LiveRuntimeIdentity identity,
    IOptions<BotRuntimeOptions> options,
    TimeProvider clock,
    ILogger<BotRuntimeHost> logger)
    : BackgroundService
{
    private readonly Dictionary<BotId, BotRunner> _runners = [];

    private LeaseHolderId HolderId => identity.HolderId;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.PollingInterval, clock);

        try
        {
            do
            {
                try
                {
                    await Pass(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError("The bot runtime pass failed with {ExceptionType}.", exception.GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
        finally
        {
            foreach (BotRunner runner in _runners.Values)
            {
                runner.StopAfterCurrentRoot();
            }

            try
            {
                await Task.WhenAll(_runners.Values.Select(runner => runner.Completion));
            }
            catch (Exception exception)
            {
                logger.LogError("Stopping the bot runners failed with {ExceptionType}.", exception.GetType().Name);
            }
        }
    }

    private async Task Pass(CancellationToken stoppingToken)
    {
        using IServiceScope scope = scopes.CreateScope();
        await Beat(scope.ServiceProvider.GetRequiredService<NpgsqlConnection>(), stoppingToken);
        var bots = scope.ServiceProvider.GetRequiredService<IBotRepository>();
        HashSet<BotId> desired = [.. await bots.ListDesiredRunning(stoppingToken)];
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        foreach ((BotId id, BotRunner runner) in _runners.ToArray())
        {
            if (runner.Completion.IsCompleted || !desired.Contains(id))
            {
                runner.StopAfterCurrentRoot();
                await runner.Completion;
                _runners.Remove(id);

                continue;
            }

            Bot? bot = await bots.Get(id, stoppingToken);

            if (bot is { IsDegraded: true, StatusReason: { } reason } && BotStatusReasons.IsSafety(reason))
            {
                runner.StopAfterCurrentRoot(preserveStatus: true);
                await runner.Completion;
                _runners.Remove(id);
            }
            else if (runner.IsTrading && bot is { IsDegraded: false, RuntimeStatus: not BotRuntimeStatus.Running })
            {
                await sender.Send(new ReportBotRuntimeCommand(id.Value, HolderId.Value, runner.Epoch, BotRuntimeReport.Running), stoppingToken);
            }
        }

        foreach (BotId id in desired.Where(id => !_runners.ContainsKey(id)))
        {
            try
            {
                await TryRun(scope.ServiceProvider, bots, sender, id, stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError("Bot {BotId} could not be leased with {ExceptionType}.", id.Value, exception.GetType().Name);
            }
        }
    }

    private async Task TryRun(IServiceProvider services, IBotRepository bots, ISender sender, BotId id, CancellationToken stoppingToken)
    {
        long? epoch = await sender.Send(new AcquireBotLeaseCommand(id.Value, HolderId.Value, options.Value.LeaseDuration), stoppingToken);

        if (epoch is not { } leased)
        {
            return;
        }

        Bot bot = await bots.Get(id, stoppingToken) ?? throw new InvalidOperationException("A leased bot does not exist.");

        if (services.GetRequiredService<VenuePorts<IOrderGateway>>().Find(bot.Series.Venue) is null)
        {
            await sender.Send(new ReportBotRuntimeCommand(id.Value, HolderId.Value, leased, BotRuntimeReport.Degraded, BotStatusReason.NoGateway), stoppingToken);
            await sender.Send(new ReleaseBotLeaseCommand(id.Value, HolderId.Value, leased), stoppingToken);
            logger.LogWarning("Bot {BotId} cannot run: trading is disabled in this process.", id.Value);

            return;
        }

        var runner = new BotRunner(bot, HolderId, leased, scopes, options.Value, clock, logger);
        _runners.Add(id, runner);
        runner.Start(stoppingToken);
    }

    private async Task Beat(NpgsqlConnection connection, CancellationToken stoppingToken)
    {
        await connection.ExecuteAsync(
            new CommandDefinition(
                commandText: """
                             INSERT INTO worker_heartbeats (name, beat_at) VALUES (@name, @now)
                             ON CONFLICT (name) DO UPDATE SET beat_at = EXCLUDED.beat_at
                             """,
                new { name = BotRuntimeOptions.HeartbeatName, now = clock.GetUtcNow() },
                cancellationToken: stoppingToken));
    }
}