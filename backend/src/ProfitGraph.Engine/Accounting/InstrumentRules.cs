namespace ProfitGraph.Engine.Accounting;

/// <summary>
///     Замороженный снимок правил инструмента для одного прогона: шаг количества, шаг цены, минимумы
///     и точность базового и котируемого актива (число знаков после запятой). Комиссии округляются вверх до точности
///     своего актива, бюджет рыночной покупки — вниз до точности котируемого актива, поэтому балансы и резервы остаются
///     точными decimal без накопления ошибки округления. Снимок не выдаётся за исторически точные правила биржи.
/// </summary>
public sealed record InstrumentRules
{
    private const int MaxPrecision = 28;

    public decimal QuantityStep { get; }
    public decimal PriceTick { get; }
    public decimal MinQuantity { get; }
    public decimal MinNotional { get; }
    public int BasePrecision { get; }
    public int QuotePrecision { get; }

    public FeeAsset BuyFeeAsset { get; }

    public bool ExactQuoteFees { get; }

    public InstrumentRules(
        decimal quantityStep,
        decimal priceTick,
        decimal minQuantity,
        decimal minNotional,
        int basePrecision,
        int quotePrecision,
        FeeAsset buyFeeAsset = FeeAsset.Base,
        bool exactQuoteFees = false)
    {
        if (quantityStep <= 0m || priceTick <= 0m || minQuantity < 0m || minNotional < 0m)
        {
            throw new TradingInvariantException("Instrument steps must be positive and minimums non-negative.");
        }

        if (basePrecision is < 0 or > MaxPrecision || quotePrecision is < 0 or > MaxPrecision)
        {
            throw new TradingInvariantException($"Asset precision must be between 0 and {MaxPrecision} decimal places.");
        }

        QuantityStep = quantityStep;
        PriceTick = priceTick;
        MinQuantity = minQuantity;
        MinNotional = minNotional;
        BasePrecision = basePrecision;
        QuotePrecision = quotePrecision;
        BuyFeeAsset = buyFeeAsset;
        ExactQuoteFees = exactQuoteFees;

        if (!Enum.IsDefined(buyFeeAsset))
        {
            throw new TradingInvariantException("Unknown instrument execution policy.");
        }
    }
}