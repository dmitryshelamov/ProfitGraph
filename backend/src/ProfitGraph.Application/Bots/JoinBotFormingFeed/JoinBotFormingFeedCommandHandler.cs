using MediatR;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Application.Bots.JoinBotFormingFeed;

internal sealed class JoinBotFormingFeedCommandHandler(
    IBotRepository bots,
    BotMarketDataAccess access,
    VenuePorts<IFormingCandleSource> sources,
    IFormingCandleFeeds feeds) : IRequestHandler<JoinBotFormingFeedCommand, FormingFeedSnapshotInternal>
{
    public async Task<FormingFeedSnapshotInternal> Handle(JoinBotFormingFeedCommand command, CancellationToken cancellationToken)
    {
        Bot bot = await bots.GetOwned(command.BotId, command.OwnerId, cancellationToken);

        // A connection follows one bot's feed at a time, so a bot without a forming candle still releases the earlier one.
        await feeds.Leave(command.ConnectionId);

        ExchangeCredentialSecretsInternal? credential = await access.Read(bot, cancellationToken);
        var key = new FormingFeedKeyInternal(bot.Series.Venue.Value, bot.Series.Symbol.Value, bot.Series.Timeframe.Key, credential?.CredentialId);

        return sources.Find(bot.Series.Venue) is { } source && source.Supports(bot.Series.Timeframe)
            ? await feeds.Join(key, credential, command.ConnectionId)
            : new FormingFeedSnapshotInternal(key, FormingFeedStatus.Unsupported, Candle: null);
    }
}