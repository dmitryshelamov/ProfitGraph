using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Application.Common.Ports;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Validation;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Strategies;

[NonParallelizable]
public sealed class StrategyTemplateEndpointTests
{
    private static readonly HashSet<string> BlockKinds = ["event", "condition", "order", "cancel", "grid"];

    [Test]
    public async Task TemplatesCreateEditableStrategiesWithTemplateLayoutsAndUnchangedReplay()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        var templates = (await client.GetFromJsonAsync<JsonArray>("/api/strategy-templates"))!;
        templates.Select(template => template!["kind"]!.GetValue<string>())
            .ShouldBe(["SmaCrossover", "Ladder", "LadderProfitFox", "LadderSma", "CapitulationDip", "VolumeMomentum", "LadderPlus", "DcaProfitFox", "Grid"]);
        using IServiceScope scope = application.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<INodeTypeRegistry>();
        var repository = scope.ServiceProvider.GetRequiredService<IStrategyRepository>();

        foreach (JsonNode? template in templates)
        {
            template.ShouldNotBeNull();
            var request = new JsonObject
            {
                ["name"] = template["name"]!.DeepClone(),
                ["definition"] = template["definition"]!.DeepClone(),
                ["layout"] = template["layout"]!.DeepClone()
            };
            using HttpResponseMessage response = await client.PostAsJsonAsync("/api/strategies", request);
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            var created = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
            var id = created["id"]!.GetValue<Guid>();
            StrategyVersion version = (await repository.GetVersion(new StrategyId(id), StrategyVersionNumber.First, CancellationToken.None))!;

            if (version.Definition.TemplateKind is StrategyTemplateKind.CapitulationDip or StrategyTemplateKind.VolumeMomentum)
            {
                version.Layout.Positions.Keys
                    .Order()
                    .ShouldBe(
                        version.Definition.Blocks
                            .Select(block => block.Id)
                            .Order());
                version.Layout.Positions.Count.ShouldBe(6);
                version.Layout.Positions["entryConditions"].Y.ShouldBeLessThan(version.Layout.Positions["exitConditions"].Y);
                version.Layout.Positions["exitConditions"].X.ShouldBeLessThan(version.Layout.Positions["exitSignal"].X);
                version.Layout.Positions["exitSignal"].X.ShouldBeLessThan(version.Layout.Positions["exit"].X);
            }
            else
            {
                version.Layout.Positions.ShouldBeEmpty();
            }

            StrategyGraph lowered = new StrategyDefinitionLowerer(registry.GetAll()).Lower(version.Definition).Graph;
            new GraphValidator().ValidateStrict(lowered, registry.GetAll()).Diagnostics.ShouldBeEmpty();
            JsonNode.DeepEquals(created["definition"], template["definition"]).ShouldBeTrue();
            JsonNode.DeepEquals(created["layout"], template["layout"]).ShouldBeTrue();

            StrategyDefinition expectedDefinition = template["kind"]!.GetValue<string>() switch
            {
                "CapitulationDip" => CapitulationDipStrategyBlockFactory.Create(),
                "VolumeMomentum" => VolumeMomentumStrategyBlockFactory.Create(),
                "LadderPlus" => LadderPlusStrategyBlockFactory.Create(),
                "DcaProfitFox" => ProfitFoxDcaStrategyBlockFactory.Create(),
                "LadderSma" => LadderSmaStrategyBlockFactory.Create(),
                "LadderProfitFox" => ProfitFoxLadderStrategyBlockFactory.Create(),
                "SmaCrossover" => SmaStrategyBlockFactory.Create(),
                "Grid" => GridStrategyBlockFactory.Create(),
                _ => LadderResumeStrategyBlockFactory.Create()
            };
            StrategyGraph original = new StrategyDefinitionLowerer(registry.GetAll()).Lower(expectedDefinition).Graph;
            string expected = JsonSerializer.Serialize(Replay(original, registry));
            BacktestResult actual = Replay(lowered, registry);
            JsonSerializer.Serialize(actual).ShouldBe(expected);
            actual.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);

            if (version.Definition.TemplateKind == StrategyTemplateKind.SmaCrossover)
            {
                version.Definition.Parameters.Single(parameter => parameter.Name == "fastPeriod").DefaultValue.ShouldBe(20m);
                version.Definition.Parameters.Single(parameter => parameter.Name == "slowPeriod").DefaultValue.ShouldBe(50m);
                actual.Ledger.Where(entry => entry.Kind == BacktestLedgerKind.Filled).Select(entry => entry.Fill!.Side).ShouldBe([OrderSide.Buy, OrderSide.Sell]);
            }
        }
    }

    [Test]
    public async Task TemplatesContainOnlyVisibleBlocksAndNativeWires()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        var templates = (await client.GetFromJsonAsync<JsonArray>("/api/strategy-templates"))!;
        templates[0]!["definition"]!["blocks"]!.AsArray().Count.ShouldBe(5);
        templates[1]!["name"]!.GetValue<string>().ShouldBe("Ladder");
        templates[1]!["definition"]!["templateKind"]!.GetValue<string>().ShouldBe("Ladder");
        templates[1]!["definition"]!["blocks"]!.AsArray().Count.ShouldBe(17);
        templates[2]!["name"]!.GetValue<string>().ShouldBe("Ladder (как в Profit Fox)");
        templates[2]!["definition"]!["templateKind"]!.GetValue<string>().ShouldBe("LadderV2");
        templates[2]!["definition"]!["blocks"]!.AsArray().Count.ShouldBe(17);
        templates[3]!["name"]!.GetValue<string>().ShouldBe("Ladder SMA");
        templates[3]!["definition"]!["templateKind"]!.GetValue<string>().ShouldBe("LadderSma");
        templates[3]!["definition"]!["blocks"]!.AsArray().Count.ShouldBe(18);

        templates[4]!["name"]!.GetValue<string>().ShouldBe("Откуп капитуляции");
        templates[4]!["definition"]!["templateKind"]!.GetValue<string>().ShouldBe("CapitulationDip");
        templates[4]!["definition"]!["blocks"]!.AsArray().Count.ShouldBe(6);
        templates[5]!["name"]!.GetValue<string>().ShouldBe("Импульс на объёме");
        templates[5]!["definition"]!["templateKind"]!.GetValue<string>().ShouldBe("VolumeMomentum");
        templates[5]!["definition"]!["blocks"]!.AsArray().Count.ShouldBe(6);
        templates[6]!["name"]!.GetValue<string>().ShouldBe("Ladder+");
        templates[6]!["definition"]!["templateKind"]!.GetValue<string>().ShouldBe("LadderPlus");
        templates[6]!["definition"]!["blocks"]!.AsArray().Count.ShouldBe(23);
        templates[7]!["name"]!.GetValue<string>().ShouldBe("DCA (как в Profit Fox)");
        templates[7]!["definition"]!["templateKind"]!.GetValue<string>().ShouldBe("DcaProfitFox");
        templates[7]!["definition"]!["blocks"]!.AsArray().Count.ShouldBe(6);

        foreach (JsonNode? template in templates)
        {
            JsonObject positions = template!["layout"]!["positions"]!.AsObject();

            if (template["kind"]!.GetValue<string>() is "CapitulationDip" or "VolumeMomentum")
            {
                positions
                    .Select(position => position.Key)
                    .Order()
                    .ShouldBe(["clock", "entry", "entryConditions", "exit", "exitConditions", "exitSignal"]);
            }
            else
            {
                positions.ShouldBeEmpty();
            }

            template["definition"]!.AsObject().ContainsKey("nodes").ShouldBeFalse();
            template["definition"]!["blocks"]!.AsArray()
                .ShouldAllBe(block => BlockKinds.Contains(block!["kind"]!.GetValue<string>()));
            template["definition"]!.ToJsonString().ShouldNotContain("MergeEvents");
        }
    }

    private static BacktestResult Replay(StrategyGraph graph, INodeTypeRegistry registry)
    {
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(graph, registry.GetAll());
        compilation.Validation.Diagnostics.ShouldBeEmpty();
        decimal[] prices =
        [
            .. Enumerable.Range(start: 0, count: 60)
                .Select(index => 100m - index)
                .Concat(Enumerable.Range(start: 0, count: 30).Select(index => 42m + index))
                .Concat(Enumerable.Range(start: 0, count: 40).Select(index => 70m - index))
        ];
        var input = new BacktestInput(
            compilation.Graph.ShouldNotBeNull(),
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, marketFeeRate: 0m, limitFeeRate: 0m),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 8),
            Timeframe.Parse("1h"),
            Array.Empty<Candle>(),
            prices.Select((price, index) => new Candle(DateTimeOffset.UnixEpoch.AddHours(index), price, price, price, price, Volume: 1m)));

        return new BacktestEngine().Run(input);
    }
}