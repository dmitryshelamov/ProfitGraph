using MediatR;

using ProfitGraph.Application.Backtesting;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.Checkpoints;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Bots.ChangeBotParameters;

/// <summary>
///     Changes a stopped bot's values and allocation (Profit Fox Designer ADR 0068). The values must match the latest
///     strategy version; with a stored run, the cap must cover what the run already committed and the edited graph
///     must compile, because the next start applies it as a graph change.
/// </summary>
internal sealed class ChangeBotParametersCommandHandler(
    IBotRepository bots,
    IBotCheckpointRepository checkpoints,
    IStrategyRepository strategies,
    BacktestCompiler compiler,
    BotGridRules gridRules,
    TimeProvider clock) : IRequestHandler<ChangeBotParametersCommand, BotInternal>
{
    public async Task<BotInternal> Handle(ChangeBotParametersCommand command, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwnedForUpdate(command.Id, command.OwnerId, cancellationToken);
        bot.EnsureStopped();
        StrategyVersion latest = await strategies.Latest(bot, cancellationToken);
        IReadOnlyDictionary<string, decimal> parameters = command.Parameters!;
        BotParameterRules.Ensure(latest.Definition, parameters);

        if (BotGridRules.HasGrid(latest.Definition))
        {
            gridRules.Ensure(bot.Series, bot.FeeRates, compiler.Compile(latest.Definition, parameters));
        }

        BotCheckpoint? checkpoint = await checkpoints.Get(bot.Id, cancellationToken);

        if (checkpoint is not null)
        {
            ExternalAccountCheckpoint account = checkpoint.Engine.Account;

            if (command.AllocationCap < account.CostBasis + account.QuoteReserved)
            {
                throw new ApplicationValidationException(
                    [new InputError(BotErrorCodes.AllocationBelowCommitted, "AllocationCap", "The allocation cap is below what the bot already committed.")]);
            }

            try
            {
                compiler.Compile(latest.Definition, parameters);
            }
            catch (ApplicationValidationException invalid)
            {
                throw new StateConflictException(
                    $"The strategy does not compile with these values: {string.Join(" ", invalid.Errors.Select(error => error.Message))}",
                    BotErrorCodes.ParametersIncompatible);
            }
        }

        bot.ChangeParameters(new BotParameters(parameters), command.AllocationCap, clock.GetUtcNow());
        await bots.Update(bot, cancellationToken);

        return bot.ToInternal();
    }
}