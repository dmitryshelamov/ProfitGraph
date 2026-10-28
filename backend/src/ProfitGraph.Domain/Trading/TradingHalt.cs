using ProfitGraph.Domain.Common;

namespace ProfitGraph.Domain.Trading;

/// <summary>
///     The global kill switch (Profit Fox Designer ADR 0056): while halted no submit is claimed and no bot starts. Turning
///     it on kills every running bot; turning it off only clears the halt. Only an Owner changes it.
/// </summary>
public sealed class TradingHalt : AggregateRoot
{
    public bool IsHalted { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private TradingHalt(bool isHalted, DateTimeOffset updatedAt)
    {
        IsHalted = isHalted;
        UpdatedAt = updatedAt;
    }

    public static TradingHalt Restore(bool isHalted, DateTimeOffset updatedAt)
    {
        return new TradingHalt(isHalted, updatedAt);
    }

    /// <summary>
    ///     Halts trading; returns whether it was running before. The caller kills the bots that should run.
    /// </summary>
    public bool Halt(DateTimeOffset now)
    {
        if (IsHalted)
        {
            return false;
        }

        IsHalted = true;
        UpdatedAt = now;
        AddDomainEvent(new TradingHalted(now));

        return true;
    }

    /// <summary>
    ///     Clears the halt; no bot starts by itself.
    /// </summary>
    public bool Resume(DateTimeOffset now)
    {
        if (!IsHalted)
        {
            return false;
        }

        IsHalted = false;
        UpdatedAt = now;
        AddDomainEvent(new TradingResumed(now));

        return true;
    }
}