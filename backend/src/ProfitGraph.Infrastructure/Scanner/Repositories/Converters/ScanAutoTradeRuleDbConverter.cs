using System.Text.Json;

using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Domain.Scanner.AutoTrades;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Scanner.Repositories.Converters;

/// <summary>
///     A rule's remembered pairs are stored as an alert watch's are; its proof as JSON with the numbers of each backtest.
/// </summary>
internal static class ScanAutoTradeRuleDbConverter
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    public static ScanAutoTradeRule ToDomain(this ScanAutoTradeRuleDb row)
    {
        Dictionary<string, ScanAlertPairDb> pairs = JsonSerializer.Deserialize<Dictionary<string, ScanAlertPairDb>>(row.Pairs) ?? [];
        ScanAutoTradeProofDb? proof = row.ProofJson is null ? null : JsonSerializer.Deserialize<ScanAutoTradeProofDb>(row.ProofJson, Options);

        return ScanAutoTradeRule.Restore(
            new ScanAutoTradeRuleId(row.Id),
            new UserId(row.OwnerId),
            new ScanPresetId(row.PresetId),
            new VenueId(row.Venue),
            new ScanAutoTradeCard(
                Enum.Parse<PositionDirection>(row.Direction),
                Enum.Parse<ScanScenario>(row.Scenario),
                row.Timeframe,
                row.MinExpectedR,
                row.MinChance),
            new ScanAutoTradeLimits(row.MaxOpenTrades, row.MaxTradesPerDay, row.DailyLossLimit),
            Enum.Parse<ScanAutoTradeMode>(row.Mode),
            proof is null ? null : new ScanAutoTradeProof(ToDomain(proof.Backtest), ToDomain(proof.OtherRegime)),
            row.PausedOn is { } paused ? DateOnly.FromDateTime(paused) : null,
            row.CheckedAt,
            pairs.ToDictionary(pair => pair.Key, pair => new ScanAlertPair(pair.Value.InResult, pair.Value.AlertedAt), StringComparer.Ordinal),
            row.ModeSince,
            row.CreatedAt,
            row.UpdatedAt);
    }

    public static object ToParameters(this ScanAutoTradeRule rule)
    {
        return new
        {
            Id = rule.Id.Value,
            OwnerId = rule.OwnerId.Value,
            PresetId = rule.PresetId.Value,
            Venue = rule.Venue.Value,
            Direction = rule.Card.Direction.ToString(),
            Scenario = rule.Card.Scenario.ToString(),
            rule.Card.Timeframe,
            rule.Card.MinExpectedR,
            rule.Card.MinChance,
            rule.Limits.MaxOpenTrades,
            rule.Limits.MaxTradesPerDay,
            rule.Limits.DailyLossLimit,
            Mode = rule.Mode.ToString(),
            ProofJson = rule.Proof is { } proof
                ? JsonSerializer.Serialize(new ScanAutoTradeProofDb(ToDb(proof.Backtest), ToDb(proof.OtherRegime)), Options)
                : null,
            PausedOn = rule.PausedOn?.ToDateTime(TimeOnly.MinValue),
            rule.CheckedAt,
            Pairs = JsonSerializer.Serialize(
                rule.Pairs.ToDictionary(pair => pair.Key, pair => new ScanAlertPairDb { InResult = pair.Value.InResult, AlertedAt = pair.Value.AlertedAt })),
            rule.ModeSince,
            rule.CreatedAt,
            rule.UpdatedAt
        };
    }

    private static ScanAutoTradeEvidence ToDomain(ScanAutoTradeEvidenceDb evidence)
    {
        return new ScanAutoTradeEvidence(
            new ScanBacktestId(evidence.BacktestId),
            [.. evidence.Trades.Zip(evidence.MeanR, (trades, meanR) => new ScanAutoTradeThird(trades, meanR))],
            evidence.MarketChange);
    }

    private static ScanAutoTradeEvidenceDb ToDb(ScanAutoTradeEvidence evidence)
    {
        return new ScanAutoTradeEvidenceDb(
            evidence.BacktestId.Value,
            [.. evidence.Thirds.Select(third => third.Trades)],
            [.. evidence.Thirds.Select(third => third.MeanR)],
            evidence.MarketChange);
    }
}