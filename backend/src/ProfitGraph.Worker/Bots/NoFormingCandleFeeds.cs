using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.JoinBotFormingFeed;
using ProfitGraph.Application.Bots.Ports;

namespace ProfitGraph.Worker.Bots;

/// <summary>
///     Stands in for the forming-candle feeds in the Worker, which serves no charts (ADR 0026): the join handler is
///     registered with the rest of the application, so its port must resolve, and no client can ever join here.
/// </summary>
internal sealed class NoFormingCandleFeeds : IFormingCandleFeeds
{
    public Task<FormingFeedSnapshotInternal> Join(FormingFeedKeyInternal key, ExchangeCredentialSecretsInternal? access, string connectionId)
    {
        return Task.FromResult(new FormingFeedSnapshotInternal(key, FormingFeedStatus.Unsupported, Candle: null));
    }

    public Task Leave(string connectionId)
    {
        return Task.CompletedTask;
    }
}
