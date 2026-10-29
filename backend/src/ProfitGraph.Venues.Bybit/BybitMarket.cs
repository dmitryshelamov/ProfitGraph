using Bybit.Net.Enums;

using ProfitGraph.Domain.MarketData;
using ProfitGraph.Venues.Bybit.MarketData;

namespace ProfitGraph.Venues.Bybit;

/// <summary>
///     One of the two Bybit markets the module serves (ADR 0061): Spot (<c>bybit</c>) and USDT perpetuals (<c>bybit-linear</c>). Both
///     share the module's options, signing and resilience; every request names the market's category.
/// </summary>
internal sealed class BybitMarket
{
    public static readonly BybitMarket Spot = new BybitMarket(new VenueId("bybit"), Category.Spot, "spot", "Spot", options => options.PublicSpotStreamAddress);

    public static readonly BybitMarket Linear = new BybitMarket(
        new VenueId("bybit-linear"),
        Category.Linear,
        "linear",
        "USDT Perpetual",
        options => options.PublicLinearStreamAddress);

    private readonly Func<BybitOptions, string> _publicStreamAddress;

    public VenueId Venue { get; }

    public Category Category { get; }

    /// <summary>
    ///     The category as Bybit's REST and stream topics spell it.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     The market for people, as the venue descriptor names it.
    /// </summary>
    public string Title { get; }

    public bool IsLinear => Category == Category.Linear;

    private BybitMarket(VenueId venue, Category category, string name, string title, Func<BybitOptions, string> publicStreamAddress)
    {
        Venue = venue;
        Category = category;
        Name = name;
        Title = title;
        _publicStreamAddress = publicStreamAddress;
    }

    public string PublicStreamAddress(BybitOptions options)
    {
        return _publicStreamAddress(options);
    }

    public static BybitMarket Of(VenueId venue)
    {
        return venue == Linear.Venue ? Linear : venue == Spot.Venue ? Spot : throw new ArgumentOutOfRangeException(nameof(venue));
    }
}