namespace ProfitGraph.Infrastructure.MarketData.Repositories.Contracts;

internal sealed class InstrumentRulesSnapshotDb
{
    public Guid Id { get; init; }
    public string Venue { get; init; } = null!;
    public string Source { get; init; } = null!;
    public string Market { get; init; } = null!;
    public string Symbol { get; init; } = null!;
    public string BaseAsset { get; init; } = null!;
    public string QuoteAsset { get; init; } = null!;
    public decimal BasePrecisionIncrement { get; init; }
    public decimal QuotePrecisionIncrement { get; init; }
    public decimal QuantityStep { get; init; }
    public decimal PriceTick { get; init; }
    public decimal MinQuantity { get; init; }
    public decimal MinNotional { get; init; }
    public int BasePrecision { get; init; }
    public int QuotePrecision { get; init; }
    public string BuyFeeAsset { get; init; } = null!;
    public bool ExactQuoteFees { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
}