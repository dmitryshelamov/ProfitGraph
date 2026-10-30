namespace ProfitGraph.Domain.Scanner;

/// <summary>
///     Which trade cards a preset's pairs offer (ADR 0069): the scanner trades with the move it finds, so a rise offers long cards and
///     a fall short ones; <see cref="Both" /> offers every card, as a pair opened without a preset does.
/// </summary>
public enum ScanTradeDirection
{
    Both,
    Long,
    Short
}
