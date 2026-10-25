using ProfitGraph.Venues.Steam.History;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

internal sealed class InMemorySteamMarketHistoryStore : ISteamMarketHistoryStore
{
    private readonly SortedDictionary<long, SteamStoredReceipt> _receipts = new SortedDictionary<long, SteamStoredReceipt>();

    public IReadOnlyList<SteamStoredReceipt> All
    {
        get
        {
            lock (_receipts)
            {
                return [.. _receipts.Values];
            }
        }
    }

    public Task<long?> Next(Guid credentialId, CancellationToken cancellationToken)
    {
        lock (_receipts)
        {
            return Task.FromResult<long?>(_receipts.Count == 0 ? null : _receipts.Keys.Max() + 1);
        }
    }

    public Task<IReadOnlyList<SteamStoredReceipt>> From(Guid credentialId, long position, CancellationToken cancellationToken)
    {
        lock (_receipts)
        {
            return Task.FromResult<IReadOnlyList<SteamStoredReceipt>>([.. _receipts.Values.Where(receipt => receipt.Position >= position)]);
        }
    }

    public async Task<bool> Append(Guid credentialId, long? expectedNext, IReadOnlyList<SteamStoredReceipt> receipts, CancellationToken cancellationToken)
    {
        if (await Next(credentialId, cancellationToken) != expectedNext)
        {
            return false;
        }

        lock (_receipts)
        {
            foreach (SteamStoredReceipt receipt in receipts)
            {
                _receipts.Add(receipt.Position, receipt);
            }
        }

        return true;
    }
}