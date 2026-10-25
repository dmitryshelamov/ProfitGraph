using System.Collections.Concurrent;

using Microsoft.Extensions.Options;

namespace ProfitGraph.Venues.Steam.Session;

/// <summary>
///     Serializes every physical Steam request per account through its <see cref="SteamAccountQueue" /> and is the only
///     place that retries: a read that failed transiently is queued again after one and then two seconds, so higher classes
///     may go first; a write, a rate limit or an unreadable answer is never retried (Profit Fox Designer ADR 0083).
/// </summary>
internal sealed class SteamRequestScheduler(IOptions<SteamOptions> options, TimeProvider clock)
{
    private readonly ConcurrentDictionary<Guid, SteamAccountQueue> _queues = new ConcurrentDictionary<Guid, SteamAccountQueue>();

    public SteamAccountQueue Queue(Guid credentialId)
    {
        return _queues.GetOrAdd(credentialId, _ => new SteamAccountQueue(options.Value, clock));
    }

    public async Task<SteamRequestResult> Execute(
        Guid credentialId,
        SteamHttpRequest request,
        Func<CancellationToken, Task<SteamRequestResult>> send,
        CancellationToken cancellationToken)
    {
        SteamAccountQueue queue = Queue(credentialId);
        SteamRequestClass requestClass = request.QueueClass;

        for (var attempt = 0;; attempt++)
        {
            if (!await queue.Enter(requestClass, cancellationToken))
            {
                return new SteamRequestResult(SteamRequestOutcome.Deferred);
            }

            var result = new SteamRequestResult(SteamRequestOutcome.NotSent);

            try
            {
                result = await send(cancellationToken);
            }
            finally
            {
                queue.Leave(requestClass, result);
            }

            if (!request.IsRead || result.Outcome != SteamRequestOutcome.TransientFailure || attempt >= options.Value.ReadRetries)
            {
                return result;
            }

            await Task.Delay(TimeSpan.FromSeconds(attempt + 1), clock, cancellationToken);
        }
    }
}