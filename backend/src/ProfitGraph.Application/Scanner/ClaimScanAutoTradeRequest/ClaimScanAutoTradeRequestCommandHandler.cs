using MediatR;

using ProfitGraph.Application.Scanner.AutoTrades;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Domain.Scanner.AutoTrades;

namespace ProfitGraph.Application.Scanner.ClaimScanAutoTradeRequest;

/// <summary>
///     A request a Worker claimed and did not finish within <see cref="ScanAutoTradeOptions.RequestLifetime" /> is taken up again and
///     expires. A claimed request names its rule's preset, whose backtests give the cards' odds (ADR 0073).
/// </summary>
internal sealed class ClaimScanAutoTradeRequestCommandHandler(
    IScanAutoTradeRequestRepository requests,
    IScanAutoTradeRuleRepository rules,
    IScanPaperTradeRepository paperTrades,
    IScanPresetRepository presets,
    ScanAutoTradeOptions options,
    TimeProvider clock) : IRequestHandler<ClaimScanAutoTradeRequestCommand, ScanAutoTradeClaimInternal?>
{
    public async Task<ScanAutoTradeClaimInternal?> Handle(ClaimScanAutoTradeRequestCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = clock.GetUtcNow();

        if ((await requests.ClaimPending(limit: 1, now - options.RequestLifetime, cancellationToken)).FirstOrDefault() is not { } request)
        {
            return null;
        }

        ScanAutoTradeRule? rule = await rules.GetForUpdate(request.RuleId, cancellationToken);
        IReadOnlyList<ScanPaperTrade> recent = rule is null ? [] : await paperTrades.ListRecent(rule.Id, ScanAutoTradeTally.DayStart(now), cancellationToken);
        ScanAutoTradeSkip? skip = request.Status == ScanAutoTradeRequestStatus.Handling
            ? ScanAutoTradeSkip.Expired
            : ScanAutoTradeGate.Check(request, rule, recent, now, options.RequestLifetime);

        if (skip is { } reason)
        {
            request.Skipped(reason, now);
        }
        else
        {
            request.Claim();
        }

        if (rule is not null)
        {
            await rules.Update(rule, cancellationToken);
        }

        await requests.Update(request, cancellationToken);
        ScanPreset? preset = skip is null && rule is not null
            ? (await presets.List(rule.OwnerId, rule.Venue, cancellationToken)).FirstOrDefault(item => item.Id == rule.PresetId)
            : null;

        return new ScanAutoTradeClaimInternal(
            request.Id.Value,
            rule?.OwnerId.Value ?? Guid.Empty,
            rule?.Venue.Value ?? string.Empty,
            request.Symbol,
            skip is null,
            preset?.Name.Value);
    }
}