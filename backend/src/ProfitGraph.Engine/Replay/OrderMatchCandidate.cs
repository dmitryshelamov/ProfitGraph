namespace ProfitGraph.Engine.Replay;

/// <summary>
///     Кандидат исполнения и текущая точка пути. Лимитная цена исполнения остаётся в ордере;
///     при гэпе или немедленном исполнении PathPrice не перемещается назад к лимиту.
/// </summary>
public sealed record OrderMatchCandidate
{
    public long OrderId { get; }
    public decimal PathPrice { get; }

    public OrderMatchCandidate(long orderId, decimal pathPrice)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pathPrice);
        OrderId = orderId;
        PathPrice = pathPrice;
    }
}