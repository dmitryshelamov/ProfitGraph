using System.Collections.Immutable;

namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Marketplace fees paid by the seller per unit (ADR 0022, C2). A price is what the buyer pays for one unit, with no
///     buyer fee. For a seller amount <c>S</c> in minor units of the quote asset, every component with a positive rate
///     floors <c>S × rate</c>, takes the minimum when that is not above it, at least one increment, and otherwise rounds
///     half up to a whole number of increments; the buyer pays <c>S</c> plus all components. With an increment of one minor
///     unit a component costs <c>max(minimum, floor(S × rate))</c>. A seller receives only whole increments. Not every buyer
///     price is reachable this way: a sell limit price is raised to the next representable price, never lowered.
/// </summary>
public sealed record SellerFeeSchedule
{
    private const int MaxComponents = 4;

    public ImmutableArray<decimal> Rates { get; }

    /// <summary>
    ///     The smallest fee of a component with a positive rate, in quote units.
    /// </summary>
    public decimal Minimum { get; }

    /// <summary>
    ///     The step of every seller amount and fee, in minor units of the quote asset: one for most wallets, a hundred for a
    ///     wallet that counts only whole units of its currency.
    /// </summary>
    public long Increment { get; }

    public SellerFeeSchedule(IEnumerable<decimal> rates, decimal minimum, long increment = 1)
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

        if (increment < 1)
        {
            throw new TradingInvariantException("The seller fee increment must be at least one minor unit.");
        }

        Minimum = minimum;
        Increment = increment;
    }

    public bool Equals(SellerFeeSchedule? other)
    {
        return other is not null && Minimum == other.Minimum && Increment == other.Increment && Rates.SequenceEqual(other.Rates);
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
    ///     What the seller receives for a unit the buyer pays <paramref name="buyerPrice" /> for: the largest seller amount in
    ///     whole increments whose buyer price does not exceed it. For a representable price the difference is exactly the fees.
    /// </summary>
    public decimal SellerAmount(decimal buyerPrice, int quotePrecision)
    {
        decimal unit = MinorUnit(quotePrecision);
        long target = ToUnits(buyerPrice, unit, exact: false);
        long low = 0;
        long high = target / Increment;

        while (low < high)
        {
            long middle = low + (high - low + 1) / 2;

            if (BuyerUnits(middle * Increment, quotePrecision) <= target)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low * Increment * unit;
    }

    /// <summary>
    ///     The smallest representable buyer price at or above <paramref name="buyerPrice" />.
    /// </summary>
    public decimal RaiseToRepresentable(decimal buyerPrice, int quotePrecision)
    {
        decimal below = SellerAmount(buyerPrice, quotePrecision);
        decimal candidate = BuyerPrice(below, quotePrecision);

        return candidate >= buyerPrice ? candidate : BuyerPrice(below + Increment * MinorUnit(quotePrecision), quotePrecision);
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
        hash.Add(Increment);

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
                total = checked(total + ComponentUnits((long)decimal.Floor(sellerUnits * rate), minimumUnits));
            }
        }

        return total;
    }

    /// <summary>
    ///     The rounding of one floored component (Profit Fox Designer ADR 0082): the minimum, then one increment, then the
    ///     nearest whole number of increments, half up.
    /// </summary>
    private long ComponentUnits(long floored, long minimumUnits)
    {
        if (floored <= minimumUnits)
        {
            return minimumUnits;
        }

        if (floored <= Increment)
        {
            return Increment;
        }

        return checked((floored + Increment / 2) / Increment * Increment);
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