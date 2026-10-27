using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Venues.TInvest.Trading;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     What the bots on the account and share hold, as the core's checkpoints would report it, or a failure to add them up.
/// </summary>
internal sealed class FixedTInvestBotPositions(BotId bot) : ITInvestBotPositions
{
    public List<VenueBotPositionInternal> Bots { get; } = [new VenueBotPositionInternal(bot.Value, "TQBR:SBER", BaseTotal: 0m)];

    public Exception? Failure { get; set; }

    public Task<IReadOnlyList<VenueBotPositionInternal>> OnAccountShare(TInvestBotContext context, CancellationToken cancellationToken)
    {
        return Failure is { } failure
            ? Task.FromException<IReadOnlyList<VenueBotPositionInternal>>(failure)
            : Task.FromResult<IReadOnlyList<VenueBotPositionInternal>>([.. Bots]);
    }

    /// <summary>
    ///     Sets the harness bot's own position.
    /// </summary>
    public void Hold(decimal baseTotal)
    {
        Bots[0] = Bots[0] with { BaseTotal = baseTotal };
    }
}