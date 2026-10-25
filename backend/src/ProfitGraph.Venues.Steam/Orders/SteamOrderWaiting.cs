using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ProfitGraph.Application.Bots.RecordOrderWaiting;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Venues.Steam.Orders;

/// <summary>
///     Reports Steam's waiting codes as the core's neutral ones of the same names (ADR 0022, ADR 0023 TC4), which the bot page
///     and feed show. The report only explains the order: a failure is logged by kind and never stops the order itself.
/// </summary>
internal sealed class SteamOrderWaiting(IServiceScopeFactory scopes, ILogger<SteamOrderWaiting> logger) : ISteamOrderWaiting
{
    public async Task Report(SteamOrderJournalEntry entry, SteamWaitingCode? code, DateTimeOffset? until, CancellationToken cancellationToken)
    {
        OrderWaitingCode? neutral = code switch
        {
            SteamWaitingCode.SingleBuyOrderSlot => OrderWaitingCode.SingleBuyOrderSlot,
            SteamWaitingCode.AwaitingConfirmation => OrderWaitingCode.AwaitingConfirmation,
            SteamWaitingCode.RateLimited => OrderWaitingCode.RateLimited,
            SteamWaitingCode.SettlementHold => OrderWaitingCode.SettlementHold,
            _ => null
        };

        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ISender>()
                .Send(
                    new RecordOrderWaitingCommand(entry.BotId, entry.EngineOrderId, neutral, neutral == OrderWaitingCode.SettlementHold ? until : null),
                    cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning("A Steam order's waiting code could not be recorded: {Failure}", exception.GetType().Name);
        }
    }
}