using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

/// <summary>
///     Independent executable ADR 0062 rules, with synchronous replay phases and Designer's declared safety differences.
/// </summary>
internal sealed class ProfitFoxLadderReferenceModel(
    InstrumentRules rules,
    decimal buyAmount,
    decimal allocationCap,
    decimal rise,
    decimal drop,
    decimal feeRate = 0m)
{
    private readonly List<ProfitFoxReferenceOrder> _orders = new List<ProfitFoxReferenceOrder>();

    private readonly List<(OrderSide Side, decimal Price, decimal Quantity, int Candle, PricePhase Phase)> _fills =
        new List<(OrderSide Side, decimal Price, decimal Quantity, int Candle, PricePhase Phase)>();

    private ProfitFoxReferenceOrder? _average;
    private ProfitFoxReferenceOrder? _exit;
    private decimal _lastBuy;
    private decimal _quote = 100m;

    public IReadOnlyList<ProfitFoxReferenceOrder> Orders => _orders;
    public IReadOnlyList<(OrderSide Side, decimal Price, decimal Quantity, int Candle, PricePhase Phase)> Fills => _fills;
    public decimal Base { get; private set; }
    public decimal Cost { get; private set; }
    public bool RaisedAverageBlockedByDeposit { get; private set; }
    public bool RetainedCostBlockedEntry { get; private set; }
    public bool SourceWouldDegradePositiveExit { get; private set; }
    public decimal SourceEntryBudget => Math.Max(buyAmount, rules.MinNotional);
    public decimal DesignerEntryBudget => Math.Max(Floor(buyAmount, Unit(rules.QuotePrecision)), Ceiling(rules.MinNotional, Unit(rules.QuotePrecision)));

    public void Run(IReadOnlyList<Candle> candles)
    {
        Entry(index: 0, PricePhase.Open, candles[0].Open);

        for (var index = 0; index < candles.Count; index++)
        {
            Candle candle = candles[index];
            Descend(index, candle.Open, candle.Low, PricePhase.OpenToLow);

            if (_exit is { Price: { } exitPrice } exit && exitPrice >= candle.Low && exitPrice <= candle.High)
            {
                _exit = null;
                _average = null;
                decimal quantity = exit.Quantity!.Value;
                decimal removedCost = Cost * quantity / Base;
                Base -= quantity;
                Cost -= removedCost;
                _quote += quantity * exitPrice - Floor(quantity * exitPrice * feeRate, Unit(rules.QuotePrecision));
                _fills.Add((OrderSide.Sell, exitPrice, quantity, index, PricePhase.LowToHigh));

                if (Floor(Base, rules.QuantityStep) == 0m)
                {
                    Entry(index, PricePhase.LowToHigh, exitPrice);
                }
            }

            Descend(index, candle.High, candle.Close, PricePhase.HighToClose);
        }
    }

    private void Entry(int index, PricePhase phase, decimal marketPrice)
    {
        decimal budget = DesignerEntryBudget;

        if (allocationCap - Cost < budget)
        {
            RetainedCostBlockedEntry = Cost > 0m && SourceEntryBudget <= allocationCap;

            return;
        }

        if (_quote < budget)
        {
            return;
        }

        _orders.Add(new ProfitFoxReferenceOrder(OrderSide.Buy, OrderType.Market, Quantity: null, Price: null, budget, "ladder.entry", index, phase, marketPrice));
        Buy(index, phase, marketPrice, Floor(budget / marketPrice, rules.QuantityStep));
    }

    private void Descend(int index, decimal from, decimal to, PricePhase phase)
    {
        while (_average is { Price: { } price } average && price <= from && price >= to)
        {
            _average = null;
            Buy(index, phase, price, average.Quantity!.Value);
            from = price;
        }
    }

    private void Buy(int index, PricePhase phase, decimal price, decimal quantity)
    {
        Base += quantity - Floor(quantity * feeRate, Unit(rules.BasePrecision));
        Cost += quantity * price;
        _quote -= quantity * price;
        _lastBuy = price;
        _fills.Add((OrderSide.Buy, price, quantity, index, phase));
        _exit = null;
        decimal exitPrice = Floor(Cost / Base * (1m + rise), rules.PriceTick);
        decimal exitQuantity = Floor(Base, rules.QuantityStep);

        if (exitQuantity <= 0m || exitQuantity < rules.MinQuantity || exitQuantity * exitPrice < rules.MinNotional)
        {
            SourceWouldDegradePositiveExit = exitQuantity > 0m;

            return;
        }

        _exit = new ProfitFoxReferenceOrder(OrderSide.Sell, OrderType.Limit, exitQuantity, exitPrice, QuoteBudget: null, "ladder.exit", index, phase, price);
        _orders.Add(_exit);
        decimal remaining = allocationCap - Cost;

        if (remaining < buyAmount || _quote < buyAmount)
        {
            return;
        }

        decimal averagePrice = Floor(_lastBuy * (1m - drop), rules.PriceTick);
        decimal averageQuantity = Floor(buyAmount / averagePrice, rules.QuantityStep);

        if (averageQuantity * averagePrice < rules.MinNotional)
        {
            averageQuantity = Ceiling(rules.MinNotional / averagePrice, rules.QuantityStep);
        }

        decimal cost = averageQuantity * averagePrice;

        if (cost > remaining)
        {
            RaisedAverageBlockedByDeposit = true;

            return;
        }

        if (averageQuantity <= 0m || averageQuantity < rules.MinQuantity || cost > _quote)
        {
            return;
        }

        _average = new ProfitFoxReferenceOrder(OrderSide.Buy, OrderType.Limit, averageQuantity, averagePrice, QuoteBudget: null, "ladder.average", index, phase, price);
        _orders.Add(_average);
    }

    private static decimal Floor(decimal value, decimal step)
    {
        return decimal.Floor(value / step) * step;
    }

    private static decimal Ceiling(decimal value, decimal step)
    {
        return decimal.Ceiling(value / step) * step;
    }

    private static decimal Unit(int precision)
    {
        var result = 1m;

        for (var index = 0; index < precision; index++)
        {
            result /= 10m;
        }

        return result;
    }
}