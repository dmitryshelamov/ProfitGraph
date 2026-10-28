namespace ProfitGraph.Domain.Notifications;

/// <summary>
///     Identifies one sender's claim on Telegram notifications (ADR 0042); never empty.
/// </summary>
public readonly record struct TelegramClaimId
{
    public Guid Value { get; }

    public TelegramClaimId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static TelegramClaimId New()
    {
        return new TelegramClaimId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}