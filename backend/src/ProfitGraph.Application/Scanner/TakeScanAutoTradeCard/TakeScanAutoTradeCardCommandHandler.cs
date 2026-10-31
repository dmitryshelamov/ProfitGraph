using MediatR;

using ProfitGraph.Application.Scanner.AutoTrades;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Domain.Scanner.AutoTrades;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Application.Scanner.TakeScanAutoTradeCard;

/// <summary>
///     The gate is checked again, since other requests of the rule may have traded meanwhile. On paper the card becomes a paper trade
///     sized by the owner's settings with the cards' costs. A card qualifies by its cautious expected R and chance (ADR 0073).
/// </summary>
internal sealed class TakeScanAutoTradeCardCommandHandler(
    IScanAutoTradeRequestRepository requests,
    IScanAutoTradeRuleRepository rules,
    IScanPaperTradeRepository paperTrades,
    ScanAutoTradeOptions options,
    TimeProvider clock) : IRequestHandler<TakeScanAutoTradeCardCommand>
{
    public async Task Handle(TakeScanAutoTradeCardCommand command, CancellationToken cancellationToken)
    {
        if (await requests.GetForUpdate(new ScanAutoTradeRequestId(command.RequestId), cancellationToken) is not { Status: ScanAutoTradeRequestStatus.Handling } request)
        {
            return;
        }

        DateTimeOffset now = clock.GetUtcNow();
        ScanAutoTradeRule? rule = await rules.GetForUpdate(request.RuleId, cancellationToken);
        IReadOnlyList<ScanPaperTrade> recent = rule is null ? [] : await paperTrades.ListRecent(rule.Id, ScanAutoTradeTally.DayStart(now), cancellationToken);

        if (ScanAutoTradeGate.Check(request, rule, recent, now, options.RequestLifetime) is { } skip)
        {
            request.Skipped(skip, now);
        }
        else
        {
            await Take(request, rule!, command.Ideas, now, cancellationToken);
        }

        if (rule is not null)
        {
            await rules.Update(rule, cancellationToken);
        }

        await requests.Update(request, cancellationToken);
    }

    private async Task Take(ScanAutoTradeRequest request, ScanAutoTradeRule rule, ScanTradeIdeasInternal ideas, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ScanTradeIdeaInternal? card = ideas.Ideas.FirstOrDefault(idea =>
            idea.Timeframe == rule.Card.Timeframe && idea.Scenario == rule.Card.Scenario && idea.Direction == rule.Card.Direction && !idea.Taken &&
            idea is { Amount: not null, Risk: not null, Odds: { } odds } && rule.Card.Accepts(odds.ExpectedRLow, odds.ChanceLow));

        if (card is not { Amount: { } amount, Risk: { } risk, Odds: { } chosen })
        {
            request.Skipped(ScanAutoTradeSkip.NoIdea, now);

            return;
        }

        var idea = new ScanTradeIdea(card.Scenario, card.Entry, card.Stop, card.Target, card.RewardRisk, card.EntryValidity, card.Direction);
        ScanPaperTrade paper = ScanPaperTrade.Open(
            rule.Id,
            rule.Venue,
            request.Symbol,
            card.Timeframe,
            idea,
            amount,
            risk,
            ideas.Costs.Costs,
            chosen.ExpectedR,
            chosen.Chance,
            request.SnapshotAt,
            now);
        await paperTrades.Add(paper, cancellationToken);
        request.TookOnPaper(paper.Id, now);
    }
}