using MediatR;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Scanner.AutoTrades;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.Converters;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Scanner;
using ProfitGraph.Domain.Scanner.AutoTrades;

namespace ProfitGraph.Application.Scanner.GetScanAutoTrade;

/// <summary>
///     The paper result counts the rule's paper trades since it entered its mode (ADR 0071, section 7).
/// </summary>
internal sealed class GetScanAutoTradeQueryHandler(
    IScanPresetRepository presets,
    IScanAutoTradeRuleRepository rules,
    IScanAutoTradeRequestRepository requests,
    IScanPaperTradeRepository paperTrades,
    ScanAutoTradeProofReader proofs,
    ScanAutoTradeOptions options,
    TimeProvider clock) : IRequestHandler<GetScanAutoTradeQuery, ScanAutoTradeInternal>
{
    public const decimal DefaultMinExpectedR = 0.1m;

    public async Task<ScanAutoTradeInternal> Handle(GetScanAutoTradeQuery query, CancellationToken cancellationToken)
    {
        var owner = new UserId(query.UserId);
        var presetId = new ScanPresetId(query.PresetId);
        ScanPreset preset = await presets.GetForUpdate(presetId, owner, cancellationToken) ??
            throw new ResourceNotFoundException("The preset does not exist.", ScanPresetErrorCodes.NotFound);
        ScanAutoTradeRule? rule = await rules.Get(presetId, owner, cancellationToken);
        decimal minExpectedR = query.MinExpectedR ?? rule?.Card.MinExpectedR ?? DefaultMinExpectedR;
        decimal? minChance = query.MinExpectedR is null ? rule?.Card.MinChance : query.MinChance;
        ScanAutoTradeEvidenceInternal evidence = await proofs.Read(query.UserId, preset, minExpectedR, minChance, cancellationToken);
        DateTimeOffset now = clock.GetUtcNow();

        if (rule is null)
        {
            return new ScanAutoTradeInternal(
                query.PresetId,
                preset.Venue.Value,
                Rule: null,
                new ScanAutoTradeTally(Open: 0, Today: 0, Loss: 0m),
                minExpectedR,
                minChance,
                evidence,
                [],
                [],
                PaperClosed: 0,
                PaperMeanR: null);
        }

        DateTimeOffset dayStart = ScanAutoTradeTally.DayStart(now);
        IReadOnlyList<ScanPaperTrade> recent = await paperTrades.ListRecent(rule.Id, rule.ModeSince < dayStart ? rule.ModeSince : dayStart, cancellationToken);
        decimal[] closed =
        [
            .. recent.Where(trade => trade.CreatedAt >= rule.ModeSince && trade.Progress.Outcome is { Filled: true, ResultR: not null })
                .Select(trade => trade.Progress.Outcome!.ResultR!.Value)
        ];

        return new ScanAutoTradeInternal(
            query.PresetId,
            preset.Venue.Value,
            rule.ToInternal(),
            ScanAutoTradeTally.Of(recent, dayStart),
            minExpectedR,
            minChance,
            evidence,
            [.. (await requests.List(rule.Id, options.ListLimit, cancellationToken)).Select(request => request.ToInternal())],
            [.. recent.Take(options.ListLimit).Select(trade => trade.ToInternal())],
            closed.Length,
            closed.Length == 0 ? null : closed.Average());
    }
}