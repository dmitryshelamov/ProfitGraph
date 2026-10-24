using System.Text.Json;

using ProfitGraph.Domain.Bots;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Infrastructure.Bots.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Bots.Repositories.Converters;

/// <summary>
///     The engine checkpoint is plain data that round-trips through default <see cref="JsonSerializer" /> (Profit Fox
///     Designer ADR 0051); the schema version names that format.
/// </summary>
internal static class BotCheckpointDbConverter
{
    internal const int SchemaVersion = 1;

    public static BotCheckpoint ToDomain(this BotCheckpointDb row)
    {
        if (row.SchemaVersion != SchemaVersion)
        {
            throw new InvalidOperationException($"Bot checkpoint schema {row.SchemaVersion} is not supported.");
        }

        GraphEngineCheckpoint engine = JsonSerializer.Deserialize<GraphEngineCheckpoint>(row.Checkpoint)
            ?? throw new InvalidOperationException("A stored bot checkpoint is empty.");

        return new BotCheckpoint(new BotId(row.BotId), engine, BotParametersDbConverter.ToParameters(row.AppliedParameters), row.UpdatedAt);
    }

    public static BotCheckpointDb ToDb(this BotCheckpoint checkpoint)
    {
        return new BotCheckpointDb
        {
            BotId = checkpoint.BotId.Value,
            SchemaVersion = SchemaVersion,
            Checkpoint = JsonSerializer.Serialize(checkpoint.Engine),
            AppliedParameters = BotParametersDbConverter.ToJson(checkpoint.AppliedParameters),
            UpdatedAt = checkpoint.UpdatedAt
        };
    }
}