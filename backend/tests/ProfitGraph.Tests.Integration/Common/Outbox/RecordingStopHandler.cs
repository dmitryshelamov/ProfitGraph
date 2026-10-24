using Dapper;

using MediatR;

using Npgsql;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Infrastructure.Common.Postgres;

namespace ProfitGraph.Tests.Integration.Common.Outbox;

/// <summary>
///     Records each delivered <see cref="BotStopped" /> in the delivery's transaction, and can fail its next calls after
///     writing, to prove that a failed delivery leaves no effect.
/// </summary>
internal sealed class RecordingStopHandler(NpgsqlConnection connection, PostgresTransactionAccessor transactions) : INotificationHandler<BotStopped>
{
    internal const string CreateTable = "CREATE TABLE IF NOT EXISTS outbox_test_effects (bot_id uuid NOT NULL, delivered_at timestamp with time zone NOT NULL)";

    private static int FailuresLeft;

    public async Task Handle(BotStopped notification, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO outbox_test_effects (bot_id, delivered_at) VALUES (@botId, now())",
                new { botId = notification.BotId.Value },
                transactions.Current,
                cancellationToken: cancellationToken));

        if (Interlocked.Decrement(ref FailuresLeft) >= 0)
        {
            throw new InvalidOperationException("The test handler fails on purpose.");
        }
    }

    internal static void FailNext(int count)
    {
        Interlocked.Exchange(ref FailuresLeft, count);
    }
}