using Microsoft.Extensions.Logging;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Venues.TInvest.Transport;

namespace ProfitGraph.Venues.TInvest.Trading;

/// <summary>
///     The safeguard before the module ends an order by itself on the exchange's own cancellation (ADR 0031): the account's
///     position in the share, free and blocked, in shares, must be at least what every bot on the account and share holds by
///     its checkpoint, in shares, plus what the order being closed adds that the bot has not booked yet. It is not an equality:
///     the owner may hold shares of their own. A shortfall, a position the broker cannot give now, a read that fails, or bots
///     whose holdings cannot all be added up fail it. It is a guard, never proof that nothing executed.
/// </summary>
internal sealed class TInvestPositionCoverage(TInvestOperationReads operations, ITInvestBotPositions bots, ILogger<TInvestPositionCoverage> logger)
{
    /// <summary>
    ///     Null when the account covers its bots; otherwise the failed check (<see cref="TInvestAbsenceChecks" />).
    ///     <paramref name="unbookedBase" /> is signed: positive for bought shares, negative for sold ones.
    /// </summary>
    public async Task<string?> Check(TInvestBotContext context, decimal unbookedBase, CancellationToken cancellationToken)
    {
        IReadOnlyList<VenueBotPositionInternal> held;

        try
        {
            held = await bots.OnAccountShare(context, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("The positions of the bots on a T-Invest account could not be added up: {Failure}", exception.GetType().Name);

            return TInvestAbsenceChecks.BotPositionsUnknown;
        }

        if (held.Any(bot => bot.BaseTotal < 0m))
        {
            return TInvestAbsenceChecks.BotPositionsUnknown;
        }

        TInvestSharePosition? position;

        try
        {
            position = await operations.PositionOf(context, cancellationToken);
        }
        catch (TInvestRpcException exception)
        {
            logger.LogWarning("The T-Invest position could not be read: {Failure}", exception.Kind);

            return TInvestAbsenceChecks.ReadFailed;
        }

        if (position is null)
        {
            return TInvestAbsenceChecks.PositionUnreadable;
        }

        return position.Total >= held.Sum(bot => bot.BaseTotal) + unbookedBase ? null : TInvestAbsenceChecks.PositionShort;
    }
}