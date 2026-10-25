using MediatR;

using ProfitGraph.Application.Backtesting;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Application.Bots.StartBot;

/// <summary>
///     Sets the bot to run. A first run compiles the latest version with the bot's values now; a stored run resumes and
///     changes graph at runtime. A stopped run that never created an order is forgotten first, so <c>OnStart</c> and the
///     current settings apply.
/// </summary>
internal sealed class StartBotCommandHandler(
    IBotRepository bots,
    IBotLeaseRepository leases,
    IBotCheckpointRepository checkpoints,
    IOrderIntentRepository intents,
    ITradingHaltRepository halts,
    IStrategyRepository strategies,
    BacktestCompiler compiler,
    TimeProvider clock) : IRequestHandler<StartBotCommand, BotInternal>
{
    public async Task<BotInternal> Handle(StartBotCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();

        // The lease is locked before the bot, as everywhere (ADR 0016); a missing or foreign bot then answers 404 as before.
        BotLease? lease = command.Id == Guid.Empty ? null : await leases.GetForUpdate(new BotId(command.Id), cancellationToken);
        Bot bot = await bots.GetOwnedForUpdate(command.Id, command.OwnerId, cancellationToken);
        TradingHalt halt = await halts.Get(cancellationToken);
        StrategyVersion latest = await strategies.Latest(bot, cancellationToken);
        BotCheckpoint? checkpoint = await checkpoints.Get(bot.Id, cancellationToken);

        if (checkpoint is not null && await CanForget(bot, checkpoint, lease, now, cancellationToken))
        {
            await checkpoints.Delete(bot.Id, cancellationToken);
            bot.ForgetUntradedRun(now);
            checkpoint = null;
        }

        if (checkpoint is null)
        {
            if (bot.HasRun)
            {
                throw new ApplicationValidationException(
                    [new InputError(BotErrorCodes.MissingCheckpoint, "Checkpoint", "The stored state of the bot's previous run is missing.")]);
            }

            BotParameterRules.Ensure(latest.Definition, bot.Parameters.Values);
            compiler.Compile(latest.Definition, bot.Parameters.Values);
        }

        if (bot.Start(latest.Id, checkpoint is not null, halt.IsHalted, now))
        {
            await bots.Update(bot, cancellationToken);
        }

        return bot.ToInternal();
    }

    private async Task<bool> CanForget(Bot bot, BotCheckpoint checkpoint, BotLease? lease, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!bot.HasRun || bot.DesiredState != BotDesiredState.Stopped || bot.RuntimeStatus is BotRuntimeStatus.Starting or BotRuntimeStatus.Running ||
            !BotRuns.IsUntraded(checkpoint.Engine))
        {
            return false;
        }

        return (lease is null || lease.IsExpired(now)) && (await intents.ListByBot(bot.Id, cancellationToken)).Count == 0;
    }
}