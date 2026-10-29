using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Scanner.Trades;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Infrastructure.Scanner.Repositories.Contracts;

using ScanTrade = ProfitGraph.Domain.Scanner.Trades.ScanTrade;

namespace ProfitGraph.Infrastructure.Scanner.Repositories.Converters;

internal static class ScanTradeDbConverter
{
    public static ScanTrade ToDomain(this ScanTradeDb row)
    {
        return ScanTrade.Restore(
            new ScanTradeId(row.Id),
            new UserId(row.OwnerId),
            new VenueId(row.Venue),
            row.Symbol,
            Enum.Parse<ScanScenario>(row.Scenario),
            row.Entry,
            row.Stop,
            row.Target,
            row.Amount,
            row.Risk,
            new BotId(row.BotId),
            row.CreatedAt,
            row.ExpiresAt,
            Enum.Parse<ScanTradeStatus>(row.Status),
            row.OpenedAt,
            row.ClosedAt,
            row.Exit is null ? null : Enum.Parse<ScanTradeExit>(row.Exit),
            row.RealizedPnl,
            row.FailureCode,
            row.BotReleased);
    }

    public static ScanTradeDb ToDb(this ScanTrade trade)
    {
        return new ScanTradeDb
        {
            Id = trade.Id.Value,
            OwnerId = trade.OwnerId.Value,
            Venue = trade.Venue.Value,
            Symbol = trade.Symbol,
            Scenario = trade.Scenario.ToString(),
            Entry = trade.Entry,
            Stop = trade.Stop,
            Target = trade.Target,
            Amount = trade.Amount,
            Risk = trade.Risk,
            BotId = trade.BotId.Value,
            CreatedAt = trade.CreatedAt,
            ExpiresAt = trade.ExpiresAt,
            Status = trade.Status.ToString(),
            OpenedAt = trade.OpenedAt,
            ClosedAt = trade.ClosedAt,
            Exit = trade.Exit?.ToString(),
            RealizedPnl = trade.RealizedPnl,
            FailureCode = trade.FailureCode,
            BotReleased = trade.BotReleased
        };
    }

    public static ScanTradeSettings ToDomain(this ScanTradeSettingsDb row)
    {
        return ScanTradeSettings.Restore(
            new UserId(row.OwnerId),
            new VenueId(row.Venue),
            new ExchangeCredentialId(row.CredentialId),
            row.Risk,
            row.MaxAmount,
            row.StrategyId is { } strategy ? new StrategyId(strategy) : null,
            row.UpdatedAt);
    }

    public static ScanTradeSettingsDb ToDb(this ScanTradeSettings settings)
    {
        return new ScanTradeSettingsDb
        {
            OwnerId = settings.OwnerId.Value,
            Venue = settings.Venue.Value,
            CredentialId = settings.CredentialId.Value,
            Risk = settings.Risk,
            MaxAmount = settings.MaxAmount,
            StrategyId = settings.StrategyId?.Value,
            UpdatedAt = settings.UpdatedAt
        };
    }
}