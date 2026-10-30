using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Converters;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Domain.Scanner.AutoTrades;
using ProfitGraph.Domain.Scanner.Trades;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Application.Scanner.SaveScanAutoTradeRule;

/// <summary>
///     A rule takes the side its preset trades (ADR 0069) and a short only where the venue allows shorts (ADR 0061).
/// </summary>
internal sealed class SaveScanAutoTradeRuleCommandHandler(
    IScanPresetRepository presets,
    IScanAutoTradeRuleRepository rules,
    IScanTradeSettingsRepository settings,
    VenueRegistry venues,
    TimeProvider clock) : IRequestHandler<SaveScanAutoTradeRuleCommand, ScanAutoTradeRuleInternal>
{
    public async Task<ScanAutoTradeRuleInternal> Handle(SaveScanAutoTradeRuleCommand command, CancellationToken cancellationToken)
    {
        var owner = new UserId(command.UserId);
        var presetId = new ScanPresetId(command.PresetId);
        ScanPreset preset = await presets.GetForUpdate(presetId, owner, cancellationToken) ??
            throw new ResourceNotFoundException("The preset does not exist.", ScanPresetErrorCodes.NotFound);

        if (command.Direction == PositionDirection.Short && venues.Find(preset.Venue.Value) is not { SupportsShort: true })
        {
            throw new StateConflictException("The venue does not allow shorts.", ScanTradeErrorCodes.ShortUnsupported);
        }

        if (preset.TradeDirection != ScanTradeDirection.Both && preset.TradeDirection.ToString() != command.Direction.ToString())
        {
            throw new StateConflictException("The rule trades the side its preset trades.", ScanAutoTradeErrorCodes.WrongDirection);
        }

        ScanAutoTradeRule? rule = await rules.GetForUpdate(presetId, owner, cancellationToken);
        decimal? lossLimit = command.DailyLossLimit ?? rule?.Limits.DailyLossLimit;

        if (lossLimit is null)
        {
            ScanTradeSettings sizing = await settings.Get(owner, preset.Venue, cancellationToken) ??
                throw new StateConflictException("Set the risk and key of the venue's trades first.", ScanTradeErrorCodes.NoSettings);
            lossLimit = ScanAutoTradeLimits.DefaultLossRisks * sizing.Risk;
        }

        var card = new ScanAutoTradeCard(command.Direction, command.Scenario, command.Timeframe, command.MinExpectedR, command.MinChance);
        var limits = new ScanAutoTradeLimits(
            command.MaxOpenTrades ?? rule?.Limits.MaxOpenTrades ?? ScanAutoTradeLimits.DefaultMaxOpenTrades,
            command.MaxTradesPerDay ?? rule?.Limits.MaxTradesPerDay ?? ScanAutoTradeLimits.DefaultMaxTradesPerDay,
            lossLimit.Value);
        DateTimeOffset now = clock.GetUtcNow();

        if (rule is null)
        {
            rule = ScanAutoTradeRule.Create(owner, presetId, preset.Venue, card, limits, now);
            await rules.Add(rule, cancellationToken);
        }
        else
        {
            rule.Change(card, limits, now);
            await rules.Update(rule, cancellationToken);
        }

        return rule.ToInternal();
    }
}