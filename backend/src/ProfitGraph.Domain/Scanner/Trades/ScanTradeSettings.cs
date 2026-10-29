using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Accounts.Credentials;
using ProfitGraph.Domain.Common;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Scanning;

namespace ProfitGraph.Domain.Scanner.Trades;

/// <summary>
///     How an owner's scanner trade cards trade on a venue (ADR 0060): the credential their bots use, the quote a stop may lose and the
///     most quote one trade may spend. <see cref="StrategyId" /> is the owner's level trade strategy, created with the first trade.
/// </summary>
public sealed class ScanTradeSettings : AggregateRoot
{
    public UserId OwnerId { get; }
    public VenueId Venue { get; }
    public ExchangeCredentialId CredentialId { get; private set; }
    public decimal Risk { get; private set; }
    public decimal MaxAmount { get; private set; }
    public StrategyId? StrategyId { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ScanTradeSettings(
        UserId ownerId,
        VenueId venue,
        ExchangeCredentialId credentialId,
        decimal risk,
        decimal maxAmount,
        StrategyId? strategyId,
        DateTimeOffset updatedAt)
    {
        Ensure(risk, maxAmount);
        OwnerId = ownerId;
        Venue = venue;
        CredentialId = credentialId;
        Risk = risk;
        MaxAmount = maxAmount;
        StrategyId = strategyId;
        UpdatedAt = updatedAt;
    }

    public static ScanTradeSettings Create(UserId ownerId, VenueId venue, ExchangeCredentialId credentialId, decimal risk, decimal maxAmount, DateTimeOffset now)
    {
        return new ScanTradeSettings(ownerId, venue, credentialId, risk, maxAmount, strategyId: null, now);
    }

    public static ScanTradeSettings Restore(
        UserId ownerId,
        VenueId venue,
        ExchangeCredentialId credentialId,
        decimal risk,
        decimal maxAmount,
        StrategyId? strategyId,
        DateTimeOffset updatedAt)
    {
        return new ScanTradeSettings(ownerId, venue, credentialId, risk, maxAmount, strategyId, updatedAt);
    }

    public void Change(ExchangeCredentialId credentialId, decimal risk, decimal maxAmount, DateTimeOffset now)
    {
        Ensure(risk, maxAmount);
        CredentialId = credentialId;
        Risk = risk;
        MaxAmount = maxAmount;
        UpdatedAt = now;
    }

    public void UseStrategy(StrategyId strategyId, DateTimeOffset now)
    {
        StrategyId = strategyId;
        UpdatedAt = now;
    }

    /// <summary>
    ///     The quote a trade from <paramref name="entry" /> to <paramref name="stop" /> spends: what loses <see cref="Risk" /> at the stop
    ///     after a fee of <paramref name="feeRate" /> on each side, at most <see cref="MaxAmount" />; and the quote it then risks.
    /// </summary>
    public (decimal Amount, decimal Risk) Size(decimal entry, decimal stop, decimal feeRate)
    {
        decimal amount = ScanTradeIdeas.Amount(Risk, entry, stop, feeRate);

        return amount <= MaxAmount ? (amount, Risk) : (MaxAmount, MaxAmount * ((entry - stop) / entry + 2 * feeRate));
    }

    private static void Ensure(decimal risk, decimal maxAmount)
    {
        if (risk <= 0m || maxAmount <= 0m)
        {
            throw new ScanTradeRuleException(ScanTradeRuleException.InvalidSettings, "The risk and the largest amount of a trade are positive.");
        }
    }
}