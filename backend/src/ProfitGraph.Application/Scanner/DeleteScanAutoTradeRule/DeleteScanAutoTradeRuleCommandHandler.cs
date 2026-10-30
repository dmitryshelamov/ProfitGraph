using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Domain.Scanner.AutoTrades;

namespace ProfitGraph.Application.Scanner.DeleteScanAutoTradeRule;

internal sealed class DeleteScanAutoTradeRuleCommandHandler(IScanAutoTradeRuleRepository rules) : IRequestHandler<DeleteScanAutoTradeRuleCommand>
{
    public async Task Handle(DeleteScanAutoTradeRuleCommand command, CancellationToken cancellationToken)
    {
        ScanAutoTradeRule rule = await rules.GetForUpdate(new ScanPresetId(command.PresetId), new UserId(command.UserId), cancellationToken) ??
            throw new ResourceNotFoundException("The preset has no auto trade rule.", ScanAutoTradeErrorCodes.NotFound);
        await rules.Delete(rule.Id, cancellationToken);
    }
}