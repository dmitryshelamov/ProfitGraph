using MediatR;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.StopBotsForVenueAccount;

internal sealed class StopBotsForVenueAccountCommandHandler(IBotRepository bots, TimeProvider clock) : IRequestHandler<StopBotsForVenueAccountCommand>
{
    public async Task Handle(StopBotsForVenueAccountCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();

        foreach (BotId id in await bots.ListDesiredRunningOnCredential(new ExchangeCredentialId(command.CredentialId), cancellationToken))
        {
            if (await bots.GetForUpdate(id, cancellationToken) is { } bot && bot.Degrade(BotStatusReason.VenueAccountProtection, now))
            {
                await bots.Update(bot, cancellationToken);
            }
        }
    }
}