using ProfitGraph.Venues.Steam.History;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

internal sealed class InMemorySteamMarketAttributions : ISteamMarketAttributions
{
    private readonly List<SteamAttribution> _attributions = new List<SteamAttribution>();

    public IReadOnlyList<SteamAttribution> All
    {
        get
        {
            lock (_attributions)
            {
                return [.. _attributions];
            }
        }
    }

    public Task<IReadOnlyList<SteamAttribution>> Of(Guid credentialId, CancellationToken cancellationToken)
    {
        return Task.FromResult(All);
    }

    public Task<bool> Attribute(Guid credentialId, IReadOnlyList<SteamAttribution> attributions, DateTimeOffset at, CancellationToken cancellationToken)
    {
        lock (_attributions)
        {
            if (attributions.Any(attribution => _attributions.Any(other => other.ReceiptId == attribution.ReceiptId)))
            {
                return Task.FromResult(false);
            }

            _attributions.AddRange(attributions);

            return Task.FromResult(true);
        }
    }
}