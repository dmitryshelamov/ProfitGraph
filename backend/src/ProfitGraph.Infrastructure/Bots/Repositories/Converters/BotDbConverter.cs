using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Market;
using ProfitGraph.Infrastructure.Backtesting;
using ProfitGraph.Infrastructure.Backtesting.Repositories.Contracts;
using ProfitGraph.Infrastructure.Backtesting.Repositories.Converters;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Bots.Repositories.Converters;

internal static class BotDbConverter
{
    public static Bot ToDomain(this BotDb row)
    {
        return Bot.Restore(
            new BotSnapshot(
                new BotId(row.Id),
                new UserId(row.UserId),
                new StrategyId(row.StrategyId),
                new StrategyVersionId(row.StrategyVersionId),
                new ExchangeCredentialId(row.ExchangeCredentialId),
                new CandleSeries(new VenueId(row.Venue), new InstrumentSymbol(row.Symbol), Timeframe.Parse(row.Timeframe)),
                BotParametersDbConverter.ToParameters(row.Parameters),
                row.AllocationCap,
                new BotRiskLimits(row.MaxOrderQuote, row.DailyLossLimitQuote),
                new BotFeeRates(row.LimitFeeRate, row.MarketFeeRate, row.FeeRatesReadAt.ToUniversalTime(), SellerFees(row.SellerFees)),
                row.AllowForeignOrders,
                Enum.Parse<BotDesiredState>(row.DesiredState),
                Enum.Parse<BotRuntimeStatus>(row.RuntimeStatus),
                row.StatusReason is null ? null : Enum.Parse<BotStatusReason>(row.StatusReason),
                (BotDiagnostics)row.Diagnostics,
                row.RestartAttempts,
                row.NextRestartAt,
                row.HasRun,
                row.FirstStartedAt,
                row.DayStartUtc is { } day ? new DailyLossBaseline(DateOnly.FromDateTime(day), row.DayStartEquity!.Value, row.DayLastEquity!.Value) : null,
                row.RejectedVersionId is { } rejected ? new StrategyVersionId(rejected) : null,
                row.CreatedAt,
                row.UpdatedAt,
                new BotVenueSettings(row.VenueSettingsVersion, row.VenueSettings),
                row.Name is null ? null : new BotName(row.Name)));
    }

    public static BotDb ToDb(this Bot bot)
    {
        BotSnapshot snapshot = bot.ToSnapshot();

        return new BotDb
        {
            Id = snapshot.Id.Value,
            UserId = snapshot.OwnerId.Value,
            StrategyId = snapshot.StrategyId.Value,
            Name = snapshot.Name?.Value,
            StrategyVersionId = snapshot.StrategyVersionId.Value,
            ExchangeCredentialId = snapshot.CredentialId.Value,
            Venue = snapshot.Series.Venue.Value,
            Symbol = snapshot.Series.Symbol.Value,
            Timeframe = snapshot.Series.Timeframe.Key,
            Parameters = BotParametersDbConverter.ToJson(snapshot.Parameters),
            AllocationCap = snapshot.AllocationCap,
            MaxOrderQuote = snapshot.RiskLimits.MaxOrderQuote,
            DailyLossLimitQuote = snapshot.RiskLimits.DailyLossLimitQuote,
            LimitFeeRate = snapshot.FeeRates.LimitRate,
            MarketFeeRate = snapshot.FeeRates.MarketRate,
            FeeRatesReadAt = snapshot.FeeRates.ReadAt,
            SellerFees = SellerFeesJson(snapshot.FeeRates.SellerFees),
            VenueSettingsVersion = snapshot.VenueSettings!.Version,
            VenueSettings = snapshot.VenueSettings.Json,
            AllowForeignOrders = snapshot.AllowForeignOrders,
            DesiredState = snapshot.DesiredState.ToString(),
            RuntimeStatus = snapshot.RuntimeStatus.ToString(),
            StatusReason = snapshot.StatusReason?.ToString(),
            Diagnostics = (int)snapshot.Diagnostics,
            RestartAttempts = snapshot.RestartAttempts,
            NextRestartAt = snapshot.NextRestartAt,
            HasRun = snapshot.HasRun,
            FirstStartedAt = snapshot.FirstStartedAt,
            DayStartUtc = snapshot.DayBaseline?.Day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified),
            DayStartEquity = snapshot.DayBaseline?.Equity,
            DayLastEquity = snapshot.DayBaseline?.LastEquity,
            RejectedVersionId = snapshot.RejectedVersionId?.Value,
            CreatedAt = snapshot.CreatedAt,
            UpdatedAt = snapshot.UpdatedAt
        };
    }

    private static SellerFeeSchedule? SellerFees(string? json)
    {
        if (json is null)
        {
            return null;
        }

        return BacktestJsonSerializer.Deserialize<SellerFeeScheduleDb>(json).ToDomain();
    }

    private static string? SellerFeesJson(SellerFeeSchedule? fees)
    {
        return fees is null ? null : BacktestJsonSerializer.Serialize(fees.ToDb());
    }
}