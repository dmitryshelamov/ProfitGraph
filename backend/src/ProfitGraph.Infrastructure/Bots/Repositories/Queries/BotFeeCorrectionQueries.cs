namespace ProfitGraph.Infrastructure.Bots.Repositories.Queries;

internal static class BotFeeCorrectionQueries
{
    internal const string Insert = """
                                   INSERT INTO bot_fee_corrections (id, bot_id, engine_order_id, previous_fee, corrected_fee, corrected_at)
                                   VALUES (@Id, @BotId, @EngineOrderId, @PreviousFee, @CorrectedFee, @CorrectedAt)
                                   ON CONFLICT (bot_id, engine_order_id, corrected_fee) DO NOTHING
                                   """;
}