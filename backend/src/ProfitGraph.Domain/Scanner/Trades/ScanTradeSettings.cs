using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.Common;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Domain.Scanner.Trades;

/// <summary>
///     How an owner's scanner trade cards trade on a venue (ADR 0060): the credential their bots use, the quote a stop may lose and the
///     most quote one trade may spend. <see cref="StrategyId" /> is the owner's level trade strategy, created with the first trade, and
///     <see cref="ShortStrategyId" /> its short mirror (ADR 0061). On a margin venue <see cref="Leverage" /> is the leverage of every
///     trade's bot; elsewhere it is null.
/// </summary>
public sealed class ScanTradeSettings : AggregateRoot
{
    public UserId OwnerId { get; }
    public VenueId Venue { get; }
    public ExchangeCredentialId CredentialId { get; private set; }
    public decimal Risk { get; private set; }
    public decimal MaxAmount { get; private set; }
    public StrategyId? StrategyId { get; private set; }
    public StrategyId? ShortStrategyId { get; private set; }
    public decimal? Leverage { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ScanTradeSettings(
        UserId ownerId,
        VenueId venue,
        ExchangeCredentialId credentialId,
        decimal risk,
        decimal maxAmount,
        StrategyId? strategyId,
        DateTimeOffset updatedAt,
        StrategyId? shortStrategyId,
        decimal? leverage)
    {
        Ensure(risk, maxAmount, leverage);
        OwnerId = ownerId;
        Venue = venue;
        CredentialId = credentialId;
        Risk = risk;
        MaxAmount = maxAmount;
        StrategyId = strategyId;
        ShortStrategyId = shortStrategyId;
        Leverage = leverage;
        UpdatedAt = updatedAt;
    }

    public static ScanTradeSettings Create(
        UserId ownerId,
        VenueId venue,
        ExchangeCredentialId credentialId,
        decimal risk,
        decimal maxAmount,
        DateTimeOffset now,
        decimal? leverage = null)
    {
        return new ScanTradeSettings(ownerId, venue, credentialId, risk, maxAmount, strategyId: null, now, shortStrategyId: null, leverage);
    }

    public static ScanTradeSettings Restore(
        UserId ownerId,
        VenueId venue,
        ExchangeCredentialId credentialId,
        decimal risk,
        decimal maxAmount,
        StrategyId? strategyId,
        DateTimeOffset updatedAt,
        StrategyId? shortStrategyId = null,
        decimal? leverage = null)
    {
        return new ScanTradeSettings(ownerId, venue, credentialId, risk, maxAmount, strategyId, updatedAt, shortStrategyId, leverage);
    }

    public void Change(ExchangeCredentialId credentialId, decimal risk, decimal maxAmount, DateTimeOffset now, decimal? leverage = null)
    {
        Ensure(risk, maxAmount, leverage);
        CredentialId = credentialId;
        Risk = risk;
        MaxAmount = maxAmount;
        Leverage = leverage;
        UpdatedAt = now;
    }

    public void UseStrategy(StrategyId strategyId, DateTimeOffset now)
    {
        StrategyId = strategyId;
        UpdatedAt = now;
    }

    /// <summary>
    ///     Records the owner's short level trade strategy (ADR 0061), created with the first short trade.
    /// </summary>
    public void UseShortStrategy(StrategyId strategyId, DateTimeOffset now)
    {
        ShortStrategyId = strategyId;
        UpdatedAt = now;
    }

    /// <summary>
    ///     The quote a trade from <paramref name="entry" /> to <paramref name="stop" /> spends: what loses <see cref="Risk" /> at the stop
    ///     with the trade's <paramref name="costs" /> (ADR 0065), at most <see cref="MaxAmount" />; and the quote it then risks.
    /// </summary>
    public (decimal Amount, decimal Risk) Size(decimal entry, decimal stop, ScanTradeCosts costs)
    {
        decimal amount = ScanTradeIdeas.Amount(Risk, entry, stop, costs);

        return amount <= MaxAmount ? (amount, Risk) : (MaxAmount, costs.Loss(MaxAmount, entry, stop));
    }

    private static void Ensure(decimal risk, decimal maxAmount, decimal? leverage)
    {
        if (risk <= 0m || maxAmount <= 0m || leverage < 1m)
        {
            throw new ScanTradeRuleException(
                ScanTradeRuleException.InvalidSettings,
                "The risk and the largest amount of a trade are positive, and a leverage is at least 1.");
        }
    }
}