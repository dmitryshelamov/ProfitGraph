using ProfitGraph.Application.MarketData.Contracts;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Infrastructure.MarketData.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.MarketData.Repositories.Converters;

internal static class InstrumentRulesSnapshotDbConverter
{
    public static InstrumentRulesSnapshotDb ToDb(this InstrumentRulesSnapshotInternal snapshot)
    {
        InstrumentRulesDefinitionInternal definition = snapshot.Definition;
        InstrumentRules rules = definition.Rules;

        return new InstrumentRulesSnapshotDb
        {
            Id = snapshot.Id,
            Venue = definition.Venue,
            Source = definition.Source,
            Market = definition.Market,
            Symbol = definition.Symbol,
            BaseAsset = definition.BaseAsset,
            QuoteAsset = definition.QuoteAsset,
            BasePrecisionIncrement = definition.BasePrecisionIncrement,
            QuotePrecisionIncrement = definition.QuotePrecisionIncrement,
            QuantityStep = rules.QuantityStep,
            PriceTick = rules.PriceTick,
            MinQuantity = rules.MinQuantity,
            MinNotional = rules.MinNotional,
            BasePrecision = rules.BasePrecision,
            QuotePrecision = rules.QuotePrecision,
            BuyFeeAsset = rules.BuyFeeAsset == FeeAsset.Base ? "base" : "quote",
            ExactQuoteFees = rules.ExactQuoteFees,
            CapturedAt = snapshot.CapturedAt
        };
    }

    public static InstrumentRulesSnapshotInternal ToInternal(this InstrumentRulesSnapshotDb row)
    {
        var rules = new InstrumentRules(
            row.QuantityStep,
            row.PriceTick,
            row.MinQuantity,
            row.MinNotional,
            row.BasePrecision,
            row.QuotePrecision,
            row.BuyFeeAsset == "base" ? FeeAsset.Base : FeeAsset.Quote,
            row.ExactQuoteFees);
        var definition = new InstrumentRulesDefinitionInternal(
            row.Venue,
            row.Source,
            row.Market,
            row.Symbol,
            row.BaseAsset,
            row.QuoteAsset,
            row.BasePrecisionIncrement,
            row.QuotePrecisionIncrement,
            rules);

        return new InstrumentRulesSnapshotInternal(row.Id, definition, row.CapturedAt.ToUniversalTime());
    }
}