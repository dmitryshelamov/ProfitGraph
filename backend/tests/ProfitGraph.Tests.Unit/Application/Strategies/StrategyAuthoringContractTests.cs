using NSubstitute;

using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Converters;
using ProfitGraph.Application.Strategies.GetBlockCatalog;
using ProfitGraph.Application.Strategies.Validators;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Graphs.Composites;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Nodes;

using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Application.Strategies;

public sealed class StrategyAuthoringContractTests
{
    [Test]
    public async Task EveryCatalogEntryLowersWithRegisteredSchemaVersions()
    {
        BlockCatalogInternal catalog = await new GetBlockCatalogQueryHandler().Handle(new GetBlockCatalogQuery(), CancellationToken.None);
        INodeDefinition[] definitions = Catalog.Select(definition =>
            {
                var replacement = Substitute.For<INodeDefinition>();
                replacement.TypeKey.Returns(definition.TypeKey);
                replacement.SchemaVersion.Returns(definition.SchemaVersion + 100);

                return replacement;
            })
            .ToArray();
        var close = new ValueExpression("candle", field: "Close");
        var period = new ValueExpression("parameter", name: "period");
        var tag = new ValueExpression("constant", ValueType.Tag, text: "tag");
        var values = new List<StrategyValue> { new StrategyValue("reference", close) };

        foreach (BlockCatalogEntryInternal entry in catalog.ValueSources)
        {
            Dictionary<string, ValueExpression> arguments = entry.Operands.ToDictionary(
                operand => operand.Key,
                operand => operand.Key == "period" ? period : operand.Key == "tag" ? tag : close);
            var expression = new ValueExpression(
                entry.Key,
                ValueType.Price,
                entry.Key == "constant" ? 1m : null,
                name: entry.Key == "parameter" ? "period" : entry.Key == "value" ? "reference" : null,
                field: entry.Key == "candle" ? "Close" : entry.Key == "position" ? "averageCost" : entry.Key == "balance" ? "quoteAvailable" : "price",
                operation: entry.Key == "arithmetic" ? "Add" : null,
                arguments: arguments);
            values.Add(new StrategyValue(entry.Key, expression));
        }

        List<StrategyBlock> blocks = catalog.EventSources.Select(entry => new StrategyBlock(entry.Key, "event", source: entry.Key)).ToList();

        foreach (BlockCatalogEntryInternal entry in catalog.CheckTypes)
        {
            Dictionary<string, ValueExpression> operands = entry.Operands.ToDictionary(operand => operand.Key, _ => close);
            blocks.Add(
                new StrategyBlock(
                    entry.Key,
                    "condition",
                    join: "all",
                    checks: [new StrategyCheck(entry.Key, operands: operands, tag: tag, side: "Buy", orderKind: "Limit")]));
        }

        foreach (BlockCatalogEntryInternal entry in catalog.OrderKinds)
        {
            foreach (string side in entry.Sides!)
            {
                foreach (string sizeMode in entry.SizeModes!)
                {
                    blocks.Add(
                        new StrategyBlock(
                            entry.Key + side + sizeMode,
                            "order",
                            orderKind: entry.Key,
                            side: side,
                            amount: close,
                            quantity: close,
                            price: close,
                            sizeMode: sizeMode));
                }
            }
        }

        blocks.Add(new StrategyBlock("cancel", "cancel", tag: tag));
        var definition = new StrategyDefinition(
            [new StrategyParameter("period", ValueType.Integer, DefaultValue: 2m)],
            values,
            blocks,
            [new StrategyWire(new StrategyExit("OnStart", "out"), "cancel"), new StrategyWire(new StrategyExit("OnCandleClosed", "out"), "cancel")]);
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(definitions).Lower(definition);

        foreach (GraphNode node in lowered.Graph.Nodes)
        {
            INodeDefinition registered = BuiltInComposites.Definitions.FirstOrDefault(item => item.TypeKey == node.TypeKey)
                ?? definitions.Single(item => item.TypeKey == node.TypeKey);
            node.SchemaVersion.ShouldBe(registered.SchemaVersion, node.TypeKey);
        }
    }

    [Test]
    public async Task CatalogPublishesIndependentExecutionSettingsFromSharedSchema()
    {
        BlockCatalogInternal catalog = await new GetBlockCatalogQueryHandler().Handle(new GetBlockCatalogQuery(), CancellationToken.None);
        BlockCatalogEntryInternal preview = catalog.CheckTypes.Single(entry => entry.Key == "OrderSizePreviewValid");
        preview.Choices.ShouldBeNull();
        preview.Sides.ShouldBe(StrategyBlockSchema.Sides);
        preview.SizeModes.ShouldBe(StrategyBlockSchema.SizeModes);
        preview.OrderKinds.ShouldBe(StrategyBlockSchema.OrderKinds);
        catalog.OrderKinds.ShouldAllBe(entry => entry.Choices == null && entry.Sides != null && entry.SizeModes != null);
    }

    [Test]
    public void CheckSizeModeRoundTripsThroughApplicationContract()
    {
        var check = new StrategyCheck("OrderSizePreviewValid", side: "Sell", orderKind: "Market", sizeMode: "amount");
        check.ToInternal().ToDomain().SizeMode.ShouldBe("amount");
    }

    [Test]
    public void SaveShapeRejectsExcessiveDepthAndTotalExpressionWorkBeforeDomainConversion()
    {
        var expression = new ValueExpressionInternal("constant");

        for (var index = 0; index <= StrategyAuthoringLimits.InlineDepth; index++)
        {
            expression = new ValueExpressionInternal("previous", Arguments: new Dictionary<string, ValueExpressionInternal> { ["in"] = expression });
        }

        var deep = new StrategyDefinitionInternal([], [new StrategyValueInternal("deep", expression)], [], []);
        new StrategyDefinitionInternalValidator().Validate(deep).IsValid.ShouldBeFalse();
        var excessive = new ValueExpressionInternal(
            "arithmetic",
            Arguments: Enumerable.Range(start: 0, StrategyAuthoringLimits.ExpressionNodes + 1)
                .ToDictionary(index => "operand" + index, _ => new ValueExpressionInternal("constant")));
        var wide = new StrategyDefinitionInternal([], [new StrategyValueInternal("wide", excessive)], [], []);
        new StrategyDefinitionInternalValidator().Validate(wide).IsValid.ShouldBeFalse();
    }
}