using ProfitGraph.Venues.Steam.Orders;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     The journal's rules in memory, the single buy-order slot of an item per account included.
/// </summary>
internal sealed class InMemorySteamOrderJournal : ISteamOrderJournal
{
    private readonly Dictionary<string, SteamOrderJournalEntry> _entries = new Dictionary<string, SteamOrderJournalEntry>(StringComparer.Ordinal);
    private readonly List<SteamAssetReservation> _reservations = new List<SteamAssetReservation>();

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

    public Task<IReadOnlyList<(Guid CredentialId, Guid BotId)>> OpenAccounts(CancellationToken cancellationToken)
    {
        lock (_entries)
        {
            return Task.FromResult<IReadOnlyList<(Guid CredentialId, Guid BotId)>>(
            [
                .. _entries.Values
                    .Where(entry => entry.State != SteamOrderJournalState.Done)
                    .GroupBy(entry => entry.CredentialId)
                    .Select(group => (group.Key, group.OrderBy(entry => entry.CreatedAt).First().BotId))
            ]);
        }
    }

    public Task<bool> AddSell(SteamOrderJournalEntry entry, IReadOnlyList<SteamAssetReservation> reservations, CancellationToken cancellationToken)
    {
        lock (_entries)
        {
            bool taken = reservations.Any(reservation => _reservations.Any(other => other.IsOpen && SameAsset(other, reservation)));

            if (taken || !_entries.TryAdd(entry.ClientOrderId, entry))
            {
                return Task.FromResult(false);
            }

            _reservations.AddRange(reservations);

            return Task.FromResult(true);
        }
    }

    public Task<IReadOnlyList<SteamAssetReservation>> Reservations(string clientOrderId, CancellationToken cancellationToken)
    {
        lock (_entries)
        {
            return Task.FromResult<IReadOnlyList<SteamAssetReservation>>(
            [
                .. _reservations
                    .Where(reservation => reservation.ClientOrderId == clientOrderId)
                    .OrderBy(reservation => reservation.UnlockAt)
                    .ThenBy(reservation => reservation.AssetId, StringComparer.Ordinal)
            ]);
        }
    }

    public Task<IReadOnlyList<SteamAssetReservation>> Reserved(Guid credentialId, CancellationToken cancellationToken)
    {
        lock (_entries)
        {
            return Task.FromResult<IReadOnlyList<SteamAssetReservation>>(
                [.. _reservations.Where(reservation => reservation.CredentialId == credentialId && reservation.IsOpen)]);
        }
    }

    public Task<bool> Transition(SteamAssetReservation next, SteamReservationState expected, CancellationToken cancellationToken)
    {
        lock (_entries)
        {
            int index = _reservations.FindIndex(reservation => reservation.ClientOrderId == next.ClientOrderId && SameAsset(reservation, next));

            if (index < 0 || _reservations[index].State != expected)
            {
                return Task.FromResult(false);
            }

            _reservations[index] = next;

            return Task.FromResult(true);
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

    private static bool SameAsset(SteamAssetReservation one, SteamAssetReservation other)
    {
        return one.CredentialId == other.CredentialId && one.AppId == other.AppId && one.ContextId == other.ContextId && one.AssetId == other.AssetId;
    }

    private bool TakesHeldSlot(SteamOrderJournalEntry entry)
    {
        return entry.HoldsSlot && _entries.Values.Any(other =>
            other.ClientOrderId != entry.ClientOrderId && other.HoldsSlot && other.CredentialId == entry.CredentialId &&
            string.Equals(other.Instrument, entry.Instrument, StringComparison.Ordinal));
    }
}