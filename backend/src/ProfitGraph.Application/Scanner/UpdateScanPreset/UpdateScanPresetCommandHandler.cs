using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Converters;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Scanner;

namespace ProfitGraph.Application.Scanner.UpdateScanPreset;

internal sealed class UpdateScanPresetCommandHandler(IScanPresetRepository presets, TimeProvider clock) : IRequestHandler<UpdateScanPresetCommand, ScanPresetInternal>
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

        if (preset.Change(name, command.Definition.ToDomainConditions(), command.Definition.ToDomainSort(), command.Definition.HideStale, clock.GetUtcNow()))
        {
            await presets.Update(preset, cancellationToken);
        }

        return preset.ToInternal();
    }
}