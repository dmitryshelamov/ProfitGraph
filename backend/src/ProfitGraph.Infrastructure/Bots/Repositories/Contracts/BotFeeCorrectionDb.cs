namespace ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

internal sealed class BotFeeCorrectionDb
{
    public Guid Id { get; init; }
    public Guid BotId { get; init; }
    public long EngineOrderId { get; init; }
    public decimal PreviousFee { get; init; }
    public decimal CorrectedFee { get; init; }
    public DateTimeOffset CorrectedAt { get; init; }
}