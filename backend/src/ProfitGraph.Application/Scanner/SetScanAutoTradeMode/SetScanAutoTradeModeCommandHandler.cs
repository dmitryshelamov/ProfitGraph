using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Scanner.AutoTrades;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Converters;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Domain.Scanner.AutoTrades;

namespace ProfitGraph.Application.Scanner.SetScanAutoTradeMode;

/// <summary>
///     Live trading is not offered yet: a rule trades only off and on paper (ADR 0071, section 10).
/// </summary>
internal sealed class SetScanAutoTradeModeCommandHandler(
    IScanPresetRepository presets,
    IScanAutoTradeRuleRepository rules,
    ScanAutoTradeProofReader proofs,
    ScanAutoTradeOptions options,
    TimeProvider clock) : IRequestHandler<SetScanAutoTradeModeCommand, ScanAutoTradeRuleInternal>
{
    public async Task<ScanAutoTradeRuleInternal> Handle(SetScanAutoTradeModeCommand command, CancellationToken cancellationToken)
    {
        var owner = new UserId(command.UserId);
        var presetId = new ScanPresetId(command.PresetId);
        ScanPreset preset = await presets.GetForUpdate(presetId, owner, cancellationToken) ??
            throw new ResourceNotFoundException("The preset does not exist.", ScanPresetErrorCodes.NotFound);
        ScanAutoTradeRule rule = await rules.GetForUpdate(presetId, owner, cancellationToken) ??
            throw new ResourceNotFoundException("The preset has no auto trade rule.", ScanAutoTradeErrorCodes.NotFound);
        DateTimeOffset now = clock.GetUtcNow();

        switch (command.Mode)
        {
            case ScanAutoTradeMode.Off:
                rule.SwitchOff(now);

                break;
            case ScanAutoTradeMode.Paper:
                ScanAutoTradeEvidenceInternal evidence = await proofs.Read(command.UserId, preset, rule.Card.MinExpectedR, rule.Card.MinChance, cancellationToken);
                ScanAutoTradeProof proof = ScanAutoTradeProofReader.Proof(evidence, rule.Card) ??
                    throw new ScanAutoTradeRuleException(
                        ScanAutoTradeRuleException.NotProven,
                        "The preset's conditions need a backtest and another one over a period in which the market moved the other way.");
                rule.SwitchToPaper(proof, options.Thresholds, now);

                break;
            default:
                throw new StateConflictException("Live auto trades are not offered yet.", ScanAutoTradeErrorCodes.LiveUnavailable);
        }

        await rules.Update(rule, cancellationToken);

        return rule.ToInternal();
    }
}