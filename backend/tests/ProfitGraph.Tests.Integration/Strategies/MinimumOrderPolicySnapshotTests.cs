using System.Text.Json;
using System.Text.Json.Nodes;

using ProfitGraph.Api.Strategies.Contracts;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Infrastructure.Strategies.Repositories.Contracts;
using ProfitGraph.Infrastructure.Strategies.Repositories.Converters;

namespace ProfitGraph.Tests.Integration.Strategies;

public sealed class MinimumOrderPolicySnapshotTests
{
    [TestCase(null)]
    [TestCase("Reject")]
    [TestCase("RaiseToExchangeMinimum")]
    public void SnapshotAndHttpRoundTripPolicyWithoutAddingDefaultFields(string? policy)
    {
        var definition = new StrategyDefinition(
            [],
            [],
            [
                new StrategyBlock("buy", "order", orderKind: "Limit", side: "Buy", minimumOrderPolicy: policy),
                new StrategyBlock(
                    "guard",
                    "condition",
                    checks:
                    [new StrategyCheck("OrderSizePreviewValid", side: "Buy", orderKind: "Limit", minimumOrderPolicy: policy)])
            ],
            []);
        var version = new StrategyVersion(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Number: 1,
            definition,
            new GraphLayout(new Dictionary<string, NodePosition>()),
            DateTimeOffset.UnixEpoch);
        var serializer = new StrategySnapshotDbConverter();
        string json = serializer.Serialize(version);
        JsonNode blocks = JsonNode.Parse(json)!["definition"]!["blocks"]!;
        string? expected = policy == "RaiseToExchangeMinimum" ? policy : null;
        blocks[0]!["minimumOrderPolicy"]?.GetValue<string>().ShouldBe(expected);
        blocks[1]!["checks"]![0]!["minimumOrderPolicy"]?.GetValue<string>().ShouldBe(expected);
        json.Contains("minimumOrderPolicy", StringComparison.Ordinal).ShouldBe(expected is not null);
        StrategyVersion restored = serializer.Deserialize(
            new StrategyVersionDb
            {
                Id = version.Id, StrategyId = version.StrategyId, Number = version.Number, CreatedAt = version.CreatedAt, SnapshotJson = json
            });
        restored.Definition.Blocks[0].MinimumOrderPolicy.ShouldBe(expected);
        restored.Definition.Blocks[1].Checks[0].MinimumOrderPolicy.ShouldBe(expected);
        string response = JsonSerializer.Serialize(
            new StrategyBlockResponse(
                "buy",
                "order",
                Checks: [new StrategyCheckResponse("OrderSizePreviewValid", MinimumOrderPolicy: expected)],
                MinimumOrderPolicy: expected),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        response.Contains("minimumOrderPolicy", StringComparison.Ordinal).ShouldBe(expected is not null);
    }
}