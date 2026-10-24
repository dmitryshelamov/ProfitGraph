using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Converters;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Infrastructure.Strategies.Repositories.Contracts;

namespace ProfitGraph.Infrastructure.Strategies.Repositories.Converters;

/// <summary>
///     Versioned JSON storage snapshot of a strategy definition and layout; serialization rules stay out of Domain and Application.
/// </summary>
internal sealed class StrategySnapshotDbConverter
{
    /// <summary>
    ///     Storage format of strategy snapshots (ADR 0007); a change ships an explicit upgrade of stored snapshots.
    /// </summary>
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Options = CreateOptions();

    public string Serialize(StrategyVersion version)
    {
        return JsonSerializer.Serialize(new StrategySnapshotDb(FormatVersion, version.Definition.ToInternal(), version.Layout.ToInternal()), Options);
    }

    public StrategyVersion Deserialize(StrategyVersionDb row)
    {
        using JsonDocument document = JsonDocument.Parse(row.SnapshotJson);

        if (!document.RootElement.TryGetProperty("formatVersion", out JsonElement format) || format.GetInt32() != FormatVersion ||
            !document.RootElement.TryGetProperty("definition", out _))
        {
            throw new StateConflictException("The stored strategy has an old format and must be recreated.", "unsupported_strategy_format");
        }

        StrategySnapshotDb snapshot = JsonSerializer.Deserialize<StrategySnapshotDb>(row.SnapshotJson, Options)
            ?? throw new InvalidOperationException("Strategy snapshot is empty.");

        return new StrategyVersion(
            new StrategyVersionId(row.Id),
            new StrategyId(row.StrategyId),
            new StrategyVersionNumber(row.Number),
            snapshot.Definition.ToDomain(),
            snapshot.Layout.ToDomain(),
            row.CreatedAt);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type == typeof(StrategyBlockInternal) || info.Type == typeof(StrategyCheckInternal))
            {
                info.Properties.Single(property => property.Name == "minimumOrderPolicy").ShouldSerialize =
                    (_, value) => value is not null and not "Reject";
            }
        });

        // jsonb canonicalizes property order, so the discriminator is not necessarily the first property.
        return new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = resolver,
            AllowOutOfOrderMetadataProperties = true,
            Converters =
            {
                new ClosedEnumJsonConverter<StrategyParameterDisplayUnit>(), new ClosedEnumJsonConverter<StrategyTemplateKind>(),
                new ClosedEnumJsonConverter<RejectionReason>()
            }
        };
    }
}