using ProfitGraph.Engine.Accounting;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.Scanner.Trades;

namespace ProfitGraph.Application.Scanner.Converters;

internal static class ScanTradeInternalConverter
{
    public static ScanTradeInternal ToInternal(this ScanTrade trade)
    {
        return new ScanTradeInternal(
            trade.Id.Value,
            trade.Venue.Value,
            trade.Symbol,
            trade.Scenario,
            trade.LevelsTimeframe.Key,
            trade.Entry,
            trade.Stop,
            trade.Target,
            trade.Amount,
            trade.Risk,
            trade.BotId.Value,
            trade.CreatedAt,
            trade.ExpiresAt,
            trade.Status,
            trade.OpenedAt,
            trade.ClosedAt,
            trade.RealizedPnl,
            trade.FailureCode,
            trade.Direction);
    }

    public static ScanTradeSettingsInternal ToInternal(this ScanTradeSettings settings)
    {
        return new ScanTradeSettingsInternal(settings.Venue.Value, settings.CredentialId.Value, settings.Risk, settings.MaxAmount, settings.Leverage);
    }
}