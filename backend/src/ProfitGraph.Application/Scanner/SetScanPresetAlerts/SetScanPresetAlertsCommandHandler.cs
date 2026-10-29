using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Converters;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Scanner;

namespace ProfitGraph.Application.Scanner.SetScanPresetAlerts;

internal sealed class SetScanPresetAlertsCommandHandler(IScanPresetRepository presets, IScanAlertWatchRepository watches, TimeProvider clock)
    : IRequestHandler<SetScanPresetAlertsCommand, ScanPresetInternal>
{
    public async Task<ScanPresetInternal> Handle(SetScanPresetAlertsCommand command, CancellationToken cancellationToken)
    {
        var owner = new UserId(command.UserId);
        var id = new ScanPresetId(command.PresetId);
        ScanPreset preset = await presets.GetForUpdate(id, owner, cancellationToken) ??
            throw new ResourceNotFoundException("The preset does not exist.", ScanPresetErrorCodes.NotFound);
        ScanAlertWatch? watch = await watches.GetForUpdate(id, cancellationToken);

        if (command.Enabled && watch is null)
        {
            await watches.Add(ScanAlertWatch.Start(id, owner, clock.GetUtcNow()), cancellationToken);
        }
        else if (!command.Enabled && watch is not null)
        {
            await watches.Delete(id, cancellationToken);
        }

        return preset.ToInternal(command.Enabled);
    }
}