using MediatR;

using ProfitGraph.Application.Backtesting;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Checkpoints;
using ProfitGraph.Domain.Bots.Fills;
using ProfitGraph.Domain.Bots.Leases;
using ProfitGraph.Domain.Bots.OrderIntents;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Domain.Trading;

namespace ProfitGraph.Application.Bots.StartBot;

/// <summary>
///     Sets the bot to run. A first run compiles the latest version with the bot's values now; a stored run resumes and
///     changes graph at runtime. A stopped run that holds and spent nothing, whose intents are all final and that left no
///     fill, is forgotten first, so <c>OnStart</c> and the current settings apply; the next run continues its order
///     identifiers. A run that starts afresh fits the bot's values to the latest version's parameters (ADR 0047).
/// </summary>
internal sealed class StartBotCommandHandler(
    IBotRepository bots,
    IBotLeaseRepository leases,
    IBotCheckpointRepository checkpoints,
    IOrderIntentRepository intents,
    IBotFillRepository fills,
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
        var fitted = false;

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

            fitted = bot.FitParameters(latest.Definition.Parameters, now);
            BotParameterRules.Ensure(latest.Definition, bot.Parameters.Values);
            compiler.Compile(latest.Definition, bot.Parameters.Values);
        }

        if (bot.Start(latest.Id, checkpoint is not null, halt.IsHalted, now) || fitted)
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

        return (lease is null || lease.IsExpired(now)) &&
            (await intents.ListByBot(bot.Id, cancellationToken)).All(intent => intent.Status is OrderIntentStatus.Sent or OrderIntentStatus.Rejected) &&
            (await fills.List(bot.Id, before: null, limit: 1, cancellationToken)).Count == 0;
    }
}