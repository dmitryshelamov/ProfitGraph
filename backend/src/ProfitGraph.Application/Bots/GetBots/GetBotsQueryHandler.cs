using MediatR;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Converters;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.GetBots;

internal sealed class GetBotsQueryHandler(IBotRepository bots) : IRequestHandler<GetBotsQuery, IReadOnlyList<BotInternal>>
{
    public async Task<IReadOnlyList<BotInternal>> Handle(GetBotsQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<Bot> owned = await bots.ListByOwner(new UserId(query.OwnerId), cancellationToken);

        return [.. owned.Select(bot => bot.ToInternal())];
    }
}