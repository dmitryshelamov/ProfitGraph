using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Jobs.Contracts;

namespace ProfitGraph.Application.MarketData.StartCandleDownload;

/// <summary>
///     Queues a history download for the Worker on behalf of the user; status and result are read through the job.
/// </summary>
public sealed record StartCandleDownloadCommand(Guid UserId, string Venue, string Symbol, string Timeframe, DateTimeOffset? From, DateTimeOffset? To)
    : ICommand<BackgroundJobInternal>;