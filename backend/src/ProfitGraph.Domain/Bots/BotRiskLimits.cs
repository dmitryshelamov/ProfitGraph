namespace ProfitGraph.Domain.Bots;

/// <summary>
///     Per-bot limits in quote currency (Profit Fox Designer ADR 0056): the largest single submit and the loss from the
///     day's starting equity that stops the bot. Both are required and positive.
/// </summary>
public sealed record BotRiskLimits
{
    public decimal MaxOrderQuote { get; }
    public decimal DailyLossLimitQuote { get; }

    public BotRiskLimits(decimal maxOrderQuote, decimal dailyLossLimitQuote)
    {
        if (maxOrderQuote <= 0m || dailyLossLimitQuote <= 0m)
        {
            throw new BotRuleException(BotRuleException.InvalidSettings, "Bot risk limits must be positive.");
        }

        MaxOrderQuote = maxOrderQuote;
        DailyLossLimitQuote = dailyLossLimitQuote;
    }
}