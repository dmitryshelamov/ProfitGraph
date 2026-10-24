namespace ProfitGraph.Infrastructure.Bots.Repositories.Queries;

internal static class TradingHaltQueries
{
    internal const string Get = "SELECT halted AS Halted, updated_at AS UpdatedAt FROM trading_halt WHERE id";

    internal const string GetForUpdate = "SELECT halted AS Halted, updated_at AS UpdatedAt FROM trading_halt WHERE id FOR UPDATE";

    internal const string Update = "UPDATE trading_halt SET halted = @Halted, updated_at = @UpdatedAt WHERE id";
}