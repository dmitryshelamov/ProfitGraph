using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Converters;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Domain.Scanner.AutoTrades;

namespace ProfitGraph.Application.Scanner.UpdateScanPreset;

/// <summary>
///     Changes the user's own preset; changed conditions switch its auto trade rule off (ADR 0071, section 5), and a preset whose alerts are on starts its watch again, so its changed
///     conditions report only pairs
///     that enter the new result later (ADR 0059).
/// </summary>
internal sealed class UpdateScanPresetCommandHandler(
    IScanPresetRepository presets,
    IScanAlertWatchRepository watches,
    IScanAutoTradeRuleRepository rules,
    TimeProvider clock)
    : IRequestHandler<UpdateScanPresetCommand, ScanPresetInternal>
{
    public async Task<ScanPresetInternal> Handle(UpdateScanPresetCommand command, CancellationToken cancellationToken)
    {
        var owner = new UserId(command.UserId);
        var id = new ScanPresetId(command.PresetId);
        ScanPreset preset = await presets.GetForUpdate(id, owner, cancellationToken) ??
            throw new ResourceNotFoundException("The preset does not exist.", ScanPresetErrorCodes.NotFound);
        var name = new ScanPresetName(command.Definition.Name);

        if (await presets.NameTaken(owner, preset.Venue, name, id, cancellationToken))
        {
            throw new StateConflictException("A preset with this name already exists.", ScanPresetErrorCodes.NameTaken);
        }

        ScanAlertWatch? watch = await watches.GetForUpdate(id, cancellationToken);
        ScanFilter before = preset.Filter;

        if (preset.Change(
                name,
                command.Definition.ToDomainConditions(),
                command.Definition.ToDomainSort(),
                command.Definition.HideStale,
                command.Definition.TradeDirection,
                clock.GetUtcNow()))
        {
            await presets.Update(preset, cancellationToken);

            if (watch is not null)
            {
                watch.Restart();
                await watches.Update(watch, cancellationToken);
            }

            if (!SameFilter(before, preset.Filter) && await rules.GetForUpdate(id, owner, cancellationToken) is { } rule)
            {
                rule.ConditionsChanged(clock.GetUtcNow());
                await rules.Update(rule, cancellationToken);
            }
        }

        return preset.ToInternal(alertsEnabled: watch is not null);
    }

    private static bool SameFilter(ScanFilter left, ScanFilter right)
    {
        return left.HideStale == right.HideStale && left.Conditions.SequenceEqual(right.Conditions);
    }
}