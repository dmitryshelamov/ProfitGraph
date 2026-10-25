namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Bought base asset that becomes sellable only after a settlement hold (ADR 0022, C5): every buy fill adds a lot that
///     unlocks at the fill's market time plus the hold, and a sell consumes unlocked lots, the earliest first. Market time
///     is the replay's explicit time, never a clock.
/// </summary>
internal sealed class SettlementHoldBook(TimeSpan hold)
{
    private readonly List<SettlementHoldLot> _lots = new List<SettlementHoldLot>();

    public void Acquire(decimal quantity, DateTimeOffset at)
    {
        if (quantity > 0m)
        {
            _lots.Add(new SettlementHoldLot(quantity, at + hold));
        }
    }

    public decimal Sellable(DateTimeOffset now)
    {
        return _lots.Where(lot => lot.UnlockAt <= now).Sum(lot => lot.Quantity);
    }

    public void Consume(decimal quantity, DateTimeOffset now)
    {
        if (quantity > Sellable(now))
        {
            throw new TradingInvariantException($"Cannot sell {quantity}: only {Sellable(now)} is past its settlement hold.");
        }

        decimal left = quantity;

        for (var index = 0; index < _lots.Count && left > 0m;)
        {
            SettlementHoldLot lot = _lots[index];

            if (lot.UnlockAt > now)
            {
                index++;

                continue;
            }

            decimal taken = Math.Min(lot.Quantity, left);
            left -= taken;

            if (taken == lot.Quantity)
            {
                _lots.RemoveAt(index);
            }
            else
            {
                _lots[index] = lot with { Quantity = lot.Quantity - taken };
                index++;
            }
        }
    }
}