using System.Runtime.CompilerServices;
using System.Threading.Channels;

using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Display prices of a venue, the test venue unless named, that a test pushes by hand.
/// </summary>
public sealed class FakeLiveMarkPriceSource(VenueId venue) : ILiveMarkPriceSource
{
    private readonly Channel<LiveMarkPriceInternal> _prices = Channel.CreateUnbounded<LiveMarkPriceInternal>();

    public VenueId Venue { get; } = venue;

    public FakeLiveMarkPriceSource() : this(new VenueId(TestHistoryVenue.WithHistory))
    {
    }

    public async IAsyncEnumerable<LiveMarkPriceInternal> Subscribe(InstrumentSymbol symbol, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (LiveMarkPriceInternal price in _prices.Reader.ReadAllAsync(cancellationToken))
        {
            yield return price;
        }
    }

    public void Push(decimal price, DateTimeOffset observedAt)
    {
        _prices.Writer.TryWrite(new LiveMarkPriceInternal(price, observedAt));
    }
}