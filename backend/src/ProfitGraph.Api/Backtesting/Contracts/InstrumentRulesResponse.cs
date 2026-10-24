namespace ProfitGraph.Api.Backtesting.Contracts;

public sealed record InstrumentRulesResponse(
    string QuantityStep,
    string PriceTick,
    string MinQuantity,
    string MinNotional,
    int BasePrecision,
    int QuotePrecision);