using System.Collections.Concurrent;

using MediatR;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.Bots.RearmBotsForVenueAccount;
using ProfitGraph.Application.Bots.StopBotsForVenueAccount;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Venues.Steam.Protection;

/// <summary>
///     Protects a Steam account from being pushed further once Steam pushes back (Profit Fox Designer ADRs 0083 and 0089).
///     A CAPTCHA or a market restriction opens it at once; refusals of one kind in a row or too many failures among the
///     recent answers open it too. Opening stops every bot of the credential and every later request of the account, even
///     one already queued; orders stay as they are. Transient noise resumes by itself after a growing wait, at most
///     <see cref="SteamOptions.SoftResumeLimit" /> times in <see cref="SteamOptions.SoftResumeWindow" /> per process; any
///     other reason waits for the owner.
/// </summary>
internal sealed class SteamAccountProtection(ISteamAccountProtectionStore store, IServiceScopeFactory scopes, IOptions<SteamOptions> options, TimeProvider clock)
    : IVenueAccountProtection
{
    private readonly ConcurrentDictionary<Guid, SteamProtectionCounters> _counters = new ConcurrentDictionary<Guid, SteamProtectionCounters>();
    private readonly ConcurrentDictionary<Guid, List<DateTimeOffset>> _automaticResumes = new ConcurrentDictionary<Guid, List<DateTimeOffset>>();

    public VenueId Venue => SteamVenue.VenueId;

    public async Task<VenueAccountProtectionInternal> Read(Guid credentialId, CancellationToken cancellationToken)
    {
        return await store.Get(credentialId, cancellationToken) is { } open
            ? new VenueAccountProtectionInternal(Open: true, open.Reason.ToString(), open.OpenedAt, AutomaticResumeAt(credentialId, open.Reason, open.OpenedAt))
            : VenueAccountProtectionInternal.Clear;
    }

    /// <summary>
    ///     The owner resumes: the protection clears, the counters and the automatic budget start over.
    /// </summary>
    public async Task Resume(Guid credentialId, CancellationToken cancellationToken)
    {
        _automaticResumes.TryRemove(credentialId, out _);
        await Clear(credentialId, cancellationToken);
    }

    /// <summary>
    ///     Whether the account must send nothing now. A protection due to resume by itself resumes here.
    /// </summary>
    public async Task<bool> Holds(Guid credentialId, CancellationToken cancellationToken)
    {
        if (await store.Get(credentialId, cancellationToken) is not { } open)
        {
            return false;
        }

        if (AutomaticResumeAt(credentialId, open.Reason, open.OpenedAt) is { } at && at <= clock.GetUtcNow())
        {
            List<DateTimeOffset> used = _automaticResumes.GetOrAdd(credentialId, _ => new List<DateTimeOffset>());

            lock (used)
            {
                used.Add(clock.GetUtcNow());
            }

            await Clear(credentialId, cancellationToken);

            return false;
        }

        return true;
    }

    public async Task Observe(Guid credentialId, SteamRequestOutcome outcome, CancellationToken cancellationToken)
    {
        SteamProtectionCounters counters = _counters.GetOrAdd(credentialId, _ => new SteamProtectionCounters(options.Value));
        SteamProtectionReason? reason;

        lock (counters)
        {
            reason = counters.Record(outcome);
        }

        if (reason is { } open && await store.Open(credentialId, open, clock.GetUtcNow(), cancellationToken))
        {
            await Send(new StopBotsForVenueAccountCommand(credentialId), cancellationToken);
        }
    }

    private static bool IsTransientNoise(SteamProtectionReason reason)
    {
        return reason is SteamProtectionReason.InvalidResponse or SteamProtectionReason.TransientFailure or SteamProtectionReason.ErrorRateExceeded;
    }

    private DateTimeOffset? AutomaticResumeAt(Guid credentialId, SteamProtectionReason reason, DateTimeOffset openedAt)
    {
        if (!IsTransientNoise(reason))
        {
            return null;
        }

        DateTimeOffset now = clock.GetUtcNow();
        int used;
        List<DateTimeOffset> resumes = _automaticResumes.GetOrAdd(credentialId, _ => new List<DateTimeOffset>());

        lock (resumes)
        {
            resumes.RemoveAll(at => at <= now - options.Value.SoftResumeWindow);
            used = resumes.Count;
        }

        return used < options.Value.SoftResumeLimit ? openedAt + options.Value.SoftResumeDelay * (used + 1) : null;
    }

    private async Task Clear(Guid credentialId, CancellationToken cancellationToken)
    {
        if (_counters.TryGetValue(credentialId, out SteamProtectionCounters? counters))
        {
            lock (counters)
            {
                counters.Reset();
            }
        }

        await store.Clear(credentialId, cancellationToken);
        await Send(new RearmBotsForVenueAccountCommand(credentialId), cancellationToken);
    }

    private async Task Send(IRequest command, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, cancellationToken);
    }
}