using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Scanner;

namespace ProfitGraph.Application.Scanner.DeleteScanPreset;

internal sealed class DeleteScanPresetCommandHandler(IScanPresetRepository presets) : IRequestHandler<DeleteScanPresetCommand>
{
    public async Task Handle(DeleteScanPresetCommand command, CancellationToken cancellationToken)
    {
        if (!await presets.Delete(new ScanPresetId(command.PresetId), new UserId(command.UserId), cancellationToken))
        {
            throw new ResourceNotFoundException("The preset does not exist.", ScanPresetErrorCodes.NotFound);
        }
    }
}