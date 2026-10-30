using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Converters;
using ProfitGraph.Application.Scanner.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Scanner;

namespace ProfitGraph.Application.Scanner.CreateScanPreset;

internal sealed class CreateScanPresetCommandHandler(IEnumerable<IMarketScanSource> sources, IScanPresetRepository presets, TimeProvider clock)
    : IRequestHandler<CreateScanPresetCommand, ScanPresetInternal>
{
    public async Task<ScanPresetInternal> Handle(CreateScanPresetCommand command, CancellationToken cancellationToken)
    {
        IMarketScanSource source = sources.Find(command.Venue);
        var owner = new UserId(command.UserId);
        var name = new ScanPresetName(command.Definition.Name);

        if (await presets.NameTaken(owner, source.Venue, name, except: null, cancellationToken))
        {
            throw new StateConflictException("A preset with this name already exists.", ScanPresetErrorCodes.NameTaken);
        }

        var preset = ScanPreset.Create(
            owner,
            source.Venue,
            name,
            command.Definition.ToDomainConditions(),
            command.Definition.ToDomainSort(),
            command.Definition.HideStale,
            clock.GetUtcNow(),
            command.Definition.TradeDirection);
        await presets.Add(preset, cancellationToken);

        return preset.ToInternal(alertsEnabled: false);
    }
}