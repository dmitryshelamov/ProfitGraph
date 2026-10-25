using MediatR;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.RearmBotsForVenueAccount;

internal sealed class RearmBotsForVenueAccountCommandHandler(IBotRepository bots, TimeProvider clock) : IRequestHandler<RearmBotsForVenueAccountCommand>
{
    public async Task Handle(RearmBotsForVenueAccountCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();

        foreach (BotId id in await bots.ListDesiredRunningOnCredential(new ExchangeCredentialId(command.CredentialId), cancellationToken))
        {
            if (await bots.GetForUpdate(id, cancellationToken) is { } bot && bot.ClearVenueAccountProtection(now))
            {
                await bots.Update(bot, cancellationToken);
            }
        }
    }
}