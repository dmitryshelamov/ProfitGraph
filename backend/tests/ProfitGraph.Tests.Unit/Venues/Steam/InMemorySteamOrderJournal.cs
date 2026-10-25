using ProfitGraph.Venues.Steam.Orders;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     The journal's rules in memory, the single buy-order slot of an item per account included.
/// </summary>
internal sealed class InMemorySteamOrderJournal : ISteamOrderJournal
{
    private readonly Dictionary<string, SteamOrderJournalEntry> _entries = new Dictionary<string, SteamOrderJournalEntry>(StringComparer.Ordinal);

    public IReadOnlyList<SteamOrderJournalEntry> All
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries.Values.OrderBy(entry => entry.CreatedAt)];
            }
        }
    }

    public Task<SteamOrderJournalEntry?> Get(string clientOrderId, CancellationToken cancellationToken)
    {
        lock (_entries)
        {
            return Task.FromResult(_entries.GetValueOrDefault(clientOrderId));
        }
    }

    public Task<bool> Add(SteamOrderJournalEntry entry, CancellationToken cancellationToken)
    {
        lock (_entries)
        {
            return Task.FromResult(!TakesHeldSlot(entry) && _entries.TryAdd(entry.ClientOrderId, entry));
        }
    }

    public Task<bool> Transition(SteamOrderJournalEntry next, SteamOrderJournalState expected, CancellationToken cancellationToken)
    {
        lock (_entries)
        {
            if (!_entries.TryGetValue(next.ClientOrderId, out SteamOrderJournalEntry? current) || current.State != expected || TakesHeldSlot(next))
            {
                return Task.FromResult(false);
            }

            _entries[next.ClientOrderId] = next;

            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<SteamOrderJournalEntry>> Open(Guid credentialId, CancellationToken cancellationToken)
    {
        lock (_entries)
        {
            return Task.FromResult<IReadOnlyList<SteamOrderJournalEntry>>(
            [
                .. _entries.Values
                    .Where(entry => entry.CredentialId == credentialId && entry.State != SteamOrderJournalState.Done)
                    .OrderBy(entry => entry.CreatedAt)
                    .ThenBy(entry => entry.ClientOrderId, StringComparer.Ordinal)
            ]);
        }
    }

    /// <summary>
    ///     Replaces an entry as a crash would leave it.
    /// </summary>
    public void Put(SteamOrderJournalEntry entry)
    {
        lock (_entries)
        {
            _entries[entry.ClientOrderId] = entry;
        }
    }

    private bool TakesHeldSlot(SteamOrderJournalEntry entry)
    {
        return entry.HoldsSlot && _entries.Values.Any(other =>
            other.ClientOrderId != entry.ClientOrderId && other.HoldsSlot && other.CredentialId == entry.CredentialId &&
            string.Equals(other.Instrument, entry.Instrument, StringComparison.Ordinal));
    }
}