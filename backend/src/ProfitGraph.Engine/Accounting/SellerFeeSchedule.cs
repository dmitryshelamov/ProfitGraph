using System.Collections.Immutable;

namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Marketplace fees paid by the seller per unit (ADR 0022, C2). A price is what the buyer pays for one unit, with no
///     buyer fee. For a seller amount <c>S</c> in minor units of the quote asset, every component with a positive rate
///     costs <c>max(minimum, floor(S × rate))</c> and the buyer pays <c>S</c> plus all components. Not every buyer price is
///     reachable this way: a sell limit price is raised to the next representable price, never lowered.
/// </summary>
public sealed record SellerFeeSchedule
{
    private const int MaxComponents = 4;

    public ImmutableArray<decimal> Rates { get; }

    /// <summary>
    ///     The smallest fee of a component with a positive rate, in quote units.
    /// </summary>
    public decimal Minimum { get; }

    public SellerFeeSchedule(IEnumerable<decimal> rates, decimal minimum)
    {
        Rates = [.. rates];

        if (Rates.Length is 0 or > MaxComponents || Rates.Any(rate => rate is < 0m or >= 1m))
        {
            throw new TradingInvariantException($"A seller fee schedule needs 1 to {MaxComponents} rates in [0, 1).");
        }

        if (minimum < 0m)
        {
            throw new TradingInvariantException("The minimum seller fee cannot be negative.");
        }

        Minimum = minimum;
    }

    public bool Equals(SellerFeeSchedule? other)
    {
        return other is not null && Minimum == other.Minimum && Rates.SequenceEqual(other.Rates);
    }

    /// <summary>
    ///     What the buyer pays for a unit whose seller receives <paramref name="sellerAmount" />.
    /// </summary>
    public decimal BuyerPrice(decimal sellerAmount, int quotePrecision)
    {
        decimal unit = MinorUnit(quotePrecision);

        return BuyerUnits(ToUnits(sellerAmount, unit, exact: true), quotePrecision) * unit;
    }

    /// <summary>
    ///     What the seller receives for a unit the buyer pays <paramref name="buyerPrice" /> for: the largest seller amount
    ///     whose buyer price does not exceed it. For a representable price the difference is exactly the fees.
    /// </summary>
    public decimal SellerAmount(decimal buyerPrice, int quotePrecision)
    {
        decimal unit = MinorUnit(quotePrecision);
        long target = ToUnits(buyerPrice, unit, exact: false);
        long low = 0;
        long high = target;

        while (low < high)
        {
            long middle = low + (high - low + 1) / 2;

            if (BuyerUnits(middle, quotePrecision) <= target)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low * unit;
    }

    /// <summary>
    ///     The smallest representable buyer price at or above <paramref name="buyerPrice" />.
    /// </summary>
    public decimal RaiseToRepresentable(decimal buyerPrice, int quotePrecision)
    {
        decimal below = SellerAmount(buyerPrice, quotePrecision);
        decimal candidate = BuyerPrice(below, quotePrecision);

        return candidate >= buyerPrice ? candidate : BuyerPrice(below + MinorUnit(quotePrecision), quotePrecision);
    }

    /// <summary>
    ///     The fee of selling <paramref name="quantity" /> units at <paramref name="buyerPrice" />, charged per unit.
    /// </summary>
    public decimal Fee(decimal quantity, decimal buyerPrice, int quotePrecision)
    {
        return quantity * (buyerPrice - SellerAmount(buyerPrice, quotePrecision));
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Minimum);

        foreach (decimal rate in Rates)
        {
            hash.Add(rate);
        }

        return hash.ToHashCode();
    }

    private long BuyerUnits(long sellerUnits, int quotePrecision)
    {
        if (sellerUnits == 0)
        {
            return 0;
        }

        long minimumUnits = ToUnits(Minimum, MinorUnit(quotePrecision), exact: true);
        long total = sellerUnits;

        foreach (decimal rate in Rates)
        {
            if (rate > 0m)
            {
                total += Math.Max(minimumUnits, (long)decimal.Floor(sellerUnits * rate));
            }
        }

        return total;
    }

    private static decimal MinorUnit(int quotePrecision)
    {
        var unit = 1m;

        for (var place = 0; place < quotePrecision; place++)
        {
            unit /= 10m;
        }

        return unit;
    }

    private static long ToUnits(decimal amount, decimal unit, bool exact)
    {
        if (amount < 0m)
        {
            throw new TradingInvariantException("Seller fee amounts cannot be negative.");
        }

        decimal units = amount / unit;

        if (exact && units != decimal.Floor(units))
        {
            throw new TradingInvariantException("Seller fee amounts must be whole minor units of the quote asset.");
        }

        return checked((long)decimal.Floor(units));
    }
}