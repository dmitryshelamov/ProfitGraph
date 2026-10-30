using ProfitGraph.Api.Common.Parsing;
using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.SaveScanAutoTradeRule;
using ProfitGraph.Application.Scanner.SetScanAutoTradeMode;
using ProfitGraph.Domain.Scanner.AutoTrades;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Api.Scanner.Converters;

internal static class ScanAutoTradeResponseConverter
{
    private const int Decimals = 4;

    public static SaveScanAutoTradeRuleCommand ToCommand(this ScanAutoTradeRuleRequest request, Guid ownerId, Guid presetId)
    {
        var errors = new List<InputError>();
        var direction = Parse<PositionDirection>(request.Direction, nameof(request.Direction), errors);
        var scenario = Parse<ScanScenario>(request.Scenario, nameof(request.Scenario), errors);
        decimal minExpectedR = Number(request.MinExpectedR, nameof(request.MinExpectedR), errors) ?? 0m;
        decimal? minChance = request.MinChance is null ? null : Number(request.MinChance, nameof(request.MinChance), errors);
        decimal? lossLimit = request.DailyLossLimit is null ? null : Number(request.DailyLossLimit, nameof(request.DailyLossLimit), errors);

        return errors.Count > 0
            ? throw new ApplicationValidationException(errors)
            : new SaveScanAutoTradeRuleCommand(
                ownerId,
                presetId,
                direction,
                scenario,
                request.Timeframe,
                minExpectedR,
                minChance,
                request.MaxOpenTrades,
                request.MaxTradesPerDay,
                lossLimit);
    }

    public static SetScanAutoTradeModeCommand ToCommand(this ScanAutoTradeModeRequest request, Guid ownerId, Guid presetId)
    {
        var errors = new List<InputError>();
        var mode = Parse<ScanAutoTradeMode>(request.Mode, nameof(request.Mode), errors);

        return errors.Count > 0 ? throw new ApplicationValidationException(errors) : new SetScanAutoTradeModeCommand(ownerId, presetId, mode);
    }

    /// <summary>
    ///     A minimum from the query string, or an input error when it is not an exact number.
    /// </summary>
    public static decimal? Minimum(string? value, string field)
    {
        var errors = new List<InputError>();
        decimal? number = value is null ? null : Number(value, field, errors);

        return errors.Count > 0 ? throw new ApplicationValidationException(errors) : number;
    }

    public static ScanAutoTradeResponse ToResponse(this ScanAutoTradeInternal autoTrade)
    {
        return new ScanAutoTradeResponse(
            autoTrade.PresetId,
            autoTrade.Venue,
            autoTrade.Rule?.ToResponse(),
            autoTrade.Today.Open,
            autoTrade.Today.Today,
            Rounded(autoTrade.Today.Loss),
            Rounded(autoTrade.MinExpectedR),
            Text(autoTrade.MinChance),
            Backtest(autoTrade.Evidence.Latest, autoTrade.Evidence.LatestCandidates),
            Backtest(autoTrade.Evidence.OtherRegime, autoTrade.Evidence.OtherCandidates),
            [.. autoTrade.Requests.Select(request => request.ToResponse())],
            [.. autoTrade.PaperTrades.Select(trade => trade.ToResponse())],
            autoTrade.PaperClosed,
            Text(autoTrade.PaperMeanR));
    }

    public static ScanAutoTradeRuleResponse ToResponse(this ScanAutoTradeRuleInternal rule)
    {
        return new ScanAutoTradeRuleResponse(
            rule.Id,
            rule.PresetId,
            rule.Venue,
            rule.Direction.ToString(),
            rule.Scenario.ToString(),
            rule.Timeframe,
            ExactGraphNumberParser.Format(rule.MinExpectedR),
            Text(rule.MinChance),
            rule.MaxOpenTrades,
            rule.MaxTradesPerDay,
            ExactGraphNumberParser.Format(rule.DailyLossLimit),
            rule.Mode.ToString(),
            rule.ModeSince,
            rule.PausedOn,
            rule.Proof is { } proof ? Proof(proof.Backtest) : null,
            rule.Proof is { } other ? Proof(other.OtherRegime) : null);
    }

    private static ScanAutoTradeRequestResponse ToResponse(this ScanAutoTradeRequestInternal request)
    {
        return new ScanAutoTradeRequestResponse(
            request.Id,
            request.Symbol,
            request.SnapshotAt,
            Text(request.Price),
            request.Status,
            request.ErrorCode,
            request.TradeId,
            request.HandledAt);
    }

    private static ScanPaperTradeResponse ToResponse(this ScanPaperTradeInternal trade)
    {
        return new ScanPaperTradeResponse(
            trade.Id,
            trade.Symbol,
            trade.Timeframe,
            trade.Scenario.ToString(),
            trade.Direction.ToString(),
            ExactGraphNumberParser.Format(trade.Entry),
            ExactGraphNumberParser.Format(trade.Stop),
            ExactGraphNumberParser.Format(trade.Target),
            Rounded(trade.Amount),
            Rounded(trade.Risk),
            Rounded(trade.ExpectedR),
            trade.CreatedAt,
            trade.Status,
            trade.EnteredAt,
            trade.ExitedAt,
            Text(trade.ResultR),
            Text(trade.Pnl));
    }

    private static ScanAutoTradeBacktestResponse? Backtest(ScanBacktestEvidenceInternal? backtest, IReadOnlyList<ScanAutoTradeCandidateInternal> candidates)
    {
        return backtest is null
            ? null
            : new ScanAutoTradeBacktestResponse(
                backtest.BacktestId,
                backtest.PresetName,
                backtest.From,
                backtest.To,
                backtest.CompletedAt,
                Text(backtest.MarketChange),
                [
                    .. candidates.Select(candidate => new ScanAutoTradeCandidateResponse(
                        candidate.Timeframe,
                        candidate.Scenario.ToString(),
                        candidate.Direction.ToString(),
                        [.. candidate.Thirds.Select(Third)],
                        candidate.Qualifies))
                ]);
    }

    private static ScanAutoTradeProofResponse Proof(ScanAutoTradeEvidence evidence)
    {
        return new ScanAutoTradeProofResponse(evidence.BacktestId.Value, [.. evidence.Thirds.Select(Third)], Text(evidence.MarketChange));
    }

    private static ScanAutoTradeThirdResponse Third(ScanAutoTradeThird third)
    {
        return new ScanAutoTradeThirdResponse(third.Trades, Text(third.MeanR));
    }

    private static string? Text(decimal? value)
    {
        return value is { } number ? Rounded(number) : null;
    }

    private static string Rounded(decimal value)
    {
        return ExactGraphNumberParser.Format(Math.Round(value, Decimals, MidpointRounding.AwayFromZero));
    }

    private static T Parse<T>(string? value, string field, List<InputError> errors)
        where T : struct, Enum
    {
        if (Enum.TryParse(value, ignoreCase: false, out T parsed) && Enum.IsDefined(parsed) && !int.TryParse(value, out _))
        {
            return parsed;
        }

        errors.Add(new InputError("invalid_value", field, $"Unknown {field.ToLowerInvariant()}."));

        return default;
    }

    private static decimal? Number(string? value, string field, List<InputError> errors)
    {
        if (ExactGraphNumberParser.TryDecimal(value, out decimal number))
        {
            return number;
        }

        errors.Add(new InputError("invalid_value", field, "Expected an exact decimal number."));

        return null;
    }
}