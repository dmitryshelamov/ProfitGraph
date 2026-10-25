namespace ProfitGraph.Venues.TInvest.Persistence.Repositories.Contracts;

internal sealed class TInvestOrderAttemptDb
{
    public Guid RequestId { get; init; }

    public Guid BotId { get; init; }

    public long EngineOrderId { get; init; }

    public int Attempt { get; init; }

    public string InstrumentUid { get; init; } = string.Empty;

    public string Side { get; init; } = string.Empty;

    public string OrderType { get; init; } = string.Empty;

    public long Lots { get; init; }

    public decimal? Price { get; init; }

    public string State { get; init; } = string.Empty;

    public string? ExchangeOrderId { get; init; }

    public int? BrokerCode { get; init; }

    public DateTime CreatedAt { get; init; }

    public DateTime UpdatedAt { get; init; }
}