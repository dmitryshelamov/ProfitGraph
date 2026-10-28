using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Unit.Engine.Graphs;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

namespace ProfitGraph.Tests.Unit.Engine.Blocks;

/// <summary>
///     The upgrades that add the Ladder's restoring at resume (ADR 0040) and its reaction to a filled manual order (ADR 0038, ADR 0052)
///     to saved strategies: they add exactly what the template of today has, only to the Ladder chain they attach to, and only once.
/// </summary>
public sealed class StrategyTemplateUpgradesTests
{
    private static readonly string[] ManualBlocks = ["manualFilled", "manualCancelAverage"];

    private static IEnumerable<TestCaseData> Templates()
    {
        yield return new TestCaseData(LadderResumeStrategyBlockFactory.Create()).SetName("Ladder with resume");
        yield return new TestCaseData(ProfitFoxLadderStrategyBlockFactory.Create()).SetName("Ladder as in Profit Fox");
        yield return new TestCaseData(LadderSmaStrategyBlockFactory.Create()).SetName("Ladder SMA");
        yield return new TestCaseData(LadderPlusStrategyBlockFactory.Create()).SetName("Ladder+");
    }

    [TestCaseSource(nameof(Templates))]
    public void ASavedGraphWithoutTheManualBlocksGetsExactlyWhatTheTemplateHas(StrategyDefinition template)
    {
        StrategyDefinition saved = Without(template);

        StrategyDefinition upgraded = StrategyTemplateUpgrades.WithManualFillRebuild(saved).ShouldNotBeNull();

        upgraded.Blocks.Select(block => block.Id).Order().ShouldBe(template.Blocks.Select(block => block.Id).Order());
        upgraded.Wires.Select(Describe).Order().ShouldBe(template.Wires.Select(Describe).Order());
        upgraded.Blocks.Where(block => ManualBlocks.Contains(block.Id))
            .Select(Key)
            .Order()
            .ShouldBe(template.Blocks.Where(block => ManualBlocks.Contains(block.Id)).Select(Key).Order());
        upgraded.Parameters.ShouldBe(saved.Parameters);
        upgraded.Values.ShouldBe(saved.Values);
        upgraded.TemplateKind.ShouldBe(saved.TemplateKind);
    }

    [TestCaseSource(nameof(Templates))]
    public void AnUpgradedGraphIsNotUpgradedAgain(StrategyDefinition template)
    {
        StrategyTemplateUpgrades.WithManualFillRebuild(template).ShouldBeNull();
        StrategyTemplateUpgrades.WithManualFillRebuild(StrategyTemplateUpgrades.WithManualFillRebuild(Without(template))!).ShouldBeNull();
    }

    [Test]
    public void TheManualReactionAloneAttachesOnlyToALadderThatResumes()
    {
        StrategyTemplateUpgrades.WithManualFillRebuild(LadderStrategyBlockFactory.Create()).ShouldBeNull();
    }

    [TestCaseSource(nameof(Templates))]
    public void ALadderSavedBeforeTheResumeBlocksGetsExactlyWhatTheTemplateHas(StrategyDefinition template)
    {
        StrategyDefinition saved = WithoutResume(template);

        StrategyDefinition upgraded = StrategyTemplateUpgrades.Upgrade(saved).ShouldNotBeNull();

        upgraded.Blocks.Select(block => block.Id).Order().ShouldBe(template.Blocks.Select(block => block.Id).Order());
        upgraded.Wires.Select(Describe).Order().ShouldBe(template.Wires.Select(Describe).Order());
        upgraded.Parameters.ShouldBe(saved.Parameters);
        upgraded.Values.ShouldBe(saved.Values);
        upgraded.TemplateKind.ShouldBe(saved.TemplateKind);
        StrategyTemplateUpgrades.Upgrade(upgraded).ShouldBeNull();
    }

    [Test]
    public void ThePlainLadderGetsTheResumeBlocksAndTheManualReaction()
    {
        StrategyDefinition upgraded = StrategyTemplateUpgrades.Upgrade(LadderStrategyBlockFactory.Create()).ShouldNotBeNull();

        upgraded.Blocks.Select(block => block.Id).Order().ShouldBe(LadderResumeStrategyBlockFactory.Create().Blocks.Select(block => block.Id).Order());
        upgraded.Wires.Select(Describe).Order().ShouldBe(LadderResumeStrategyBlockFactory.Create().Wires.Select(Describe).Order());
        new StrategyDefinitionValidator().Validate(upgraded, ProductionTradingNodeTests.Catalog).Diagnostics.ShouldBeEmpty();
    }

    [TestCaseSource(nameof(Templates))]
    public void AnUpgradedGraphValidatesAgainstTheNodeRegistry(StrategyDefinition template)
    {
        StrategyDefinition upgraded = StrategyTemplateUpgrades.Upgrade(WithoutResume(template)).ShouldNotBeNull();

        new StrategyDefinitionValidator().Validate(upgraded, ProductionTradingNodeTests.Catalog).Diagnostics.ShouldBeEmpty();
    }

    [Test]
    public void AGraphThatIsNotTheLadderChainIsNotGivenTheResumeBlocks()
    {
        StrategyDefinition ladder = LadderStrategyBlockFactory.Create();
        var noExit = new StrategyDefinition(
            ladder.Parameters,
            ladder.Values,
            ladder.Blocks.Where(block => block.Id != "exit"),
            ladder.Wires.Where(wire => wire.From.Block != "exit" && wire.To != "exit"),
            ladder.TemplateKind);

        StrategyTemplateUpgrades.WithResumeRestore(noExit).ShouldBeNull();
        StrategyTemplateUpgrades.Upgrade(noExit).ShouldBeNull();
    }

    [Test]
    public void AGraphWithTheExitGuardButNoResumeEventIsLeftAsItIs()
    {
        StrategyDefinition ladder = LadderResumeStrategyBlockFactory.Create();
        var withoutResume = new StrategyDefinition(
            ladder.Parameters,
            ladder.Values,
            ladder.Blocks.Where(block => block.Id != "resume"),
            ladder.Wires.Where(wire => wire.From.Block != "resume"),
            ladder.TemplateKind);

        StrategyTemplateUpgrades.WithResumeRestore(withoutResume).ShouldBeNull();
        StrategyTemplateUpgrades.Upgrade(withoutResume).ShouldBeNull();
    }

    [Test]
    public void AGraphWithAResumeEventOfItsOwnGetsOnlyTheManualReaction()
    {
        StrategyDefinition ladder = LadderStrategyBlockFactory.Create();
        var own = new StrategyBlock("myResume", "event", "Моё возобновление", "OnResume");
        var custom = new StrategyDefinition(
            ladder.Parameters,
            ladder.Values,
            [.. ladder.Blocks, own],
            [.. ladder.Wires, new StrategyWire(new StrategyExit("myResume", "out"), "entryConditions")],
            ladder.TemplateKind);

        StrategyTemplateUpgrades.WithResumeRestore(custom).ShouldBeNull();
        StrategyDefinition upgraded = StrategyTemplateUpgrades.Upgrade(custom).ShouldNotBeNull();

        upgraded.Blocks.Select(block => block.Id).Except(custom.Blocks.Select(block => block.Id)).Order().ShouldBe(ManualBlocks.Order());
        StrategyTemplateUpgrades.Upgrade(upgraded).ShouldBeNull();
    }

    [Test]
    public void ALadderWhoseOwnerAddedTheResumeUnderOtherIdsGetsExactlyTheTemplatesManualReaction()
    {
        StrategyDefinition template = LadderResumeStrategyBlockFactory.Create();
        StrategyDefinition saved = Renamed(Without(template), ("resume", "onresume_6b61cd8a"), ("resumeExitGuard", "condition_3b69ebdd"));

        StrategyDefinition upgraded = StrategyTemplateUpgrades.Upgrade(saved).ShouldNotBeNull();

        StrategyDefinition expected = Renamed(template, ("resume", "onresume_6b61cd8a"), ("resumeExitGuard", "condition_3b69ebdd"));
        upgraded.Blocks.Select(Key).Order().ShouldBe(expected.Blocks.Select(Key).Order());
        upgraded.Wires.Select(Describe).Order().ShouldBe(expected.Wires.Select(Describe).Order());
        upgraded.Parameters.ShouldBe(saved.Parameters);
        upgraded.Values.ShouldBe(saved.Values);
        upgraded.TemplateKind.ShouldBe(saved.TemplateKind);
        new StrategyDefinitionValidator().Validate(upgraded, ProductionTradingNodeTests.Catalog).Diagnostics.ShouldBeEmpty();
        StrategyTemplateUpgrades.Upgrade(upgraded).ShouldBeNull();
    }

    [Test]
    public void AGraphWhoseChainWasChangedIsLeftAsItIs()
    {
        StrategyDefinition saved = Without(LadderSmaStrategyBlockFactory.Create());
        var withoutExitWire = new StrategyDefinition(
            saved.Parameters,
            saved.Values,
            saved.Blocks,
            saved.Wires.Where(wire => !(wire.From.Block == "exitConditions" && wire.To == "exit")),
            saved.TemplateKind);
        var withoutBlock = new StrategyDefinition(
            saved.Parameters,
            saved.Values,
            saved.Blocks.Where(block => block.Id != "averageConditions"),
            saved.Wires,
            saved.TemplateKind);

        StrategyTemplateUpgrades.WithManualFillRebuild(withoutExitWire).ShouldBeNull();
        StrategyTemplateUpgrades.WithManualFillRebuild(withoutBlock).ShouldBeNull();
    }

    [Test]
    public void AGraphThatAlreadyReactsToManualOrdersItsOwnWayIsLeftAsItIs()
    {
        StrategyDefinition saved = Without(LadderSmaStrategyBlockFactory.Create());
        var own = new StrategyBlock(
            "myManual",
            "event",
            "Моя реакция",
            "OnOrderFilled",
            tag: new ValueExpression("constant", ValueType.Tag, text: "manual"));
        var custom = new StrategyDefinition(saved.Parameters, saved.Values, [.. saved.Blocks, own], saved.Wires, saved.TemplateKind);

        StrategyTemplateUpgrades.WithManualFillRebuild(custom).ShouldBeNull();
    }

    private static StrategyDefinition WithoutResume(StrategyDefinition template)
    {
        string[] removed = [.. ManualBlocks, "resume", "resumeExitGuard"];

        return new StrategyDefinition(
            template.Parameters,
            template.Values,
            template.Blocks.Where(block => !removed.Contains(block.Id)),
            template.Wires.Where(wire => !removed.Contains(wire.From.Block) && !removed.Contains(wire.To)),
            template.TemplateKind);
    }

    private static StrategyDefinition Renamed(StrategyDefinition definition, params (string From, string To)[] renames)
    {
        Dictionary<string, string> map = renames.ToDictionary(rename => rename.From, rename => rename.To);
        string Id(string id) => map.GetValueOrDefault(id, id);

        return new StrategyDefinition(
            definition.Parameters,
            definition.Values,
            definition.Blocks.Select(
                block => new StrategyBlock(
                    Id(block.Id),
                    block.Kind,
                    block.DisplayName,
                    block.Source,
                    block.Join,
                    block.Checks,
                    block.OrderKind,
                    block.Side,
                    block.Tag,
                    block.Amount,
                    block.Quantity,
                    block.Price,
                    block.SizeMode,
                    block.MinimumOrderPolicy)),
            definition.Wires.Select(wire => new StrategyWire(new StrategyExit(Id(wire.From.Block), wire.From.Exit), Id(wire.To))),
            definition.TemplateKind);
    }

    private static StrategyDefinition Without(StrategyDefinition template)
    {
        return new StrategyDefinition(
            template.Parameters,
            template.Values,
            template.Blocks.Where(block => !ManualBlocks.Contains(block.Id)),
            template.Wires.Where(wire => !ManualBlocks.Contains(wire.From.Block) && !ManualBlocks.Contains(wire.To)),
            template.TemplateKind);
    }

    private static string Key(StrategyBlock block)
    {
        return string.Join(separator: '|', block.Id, block.Kind, block.DisplayName, block.Source, block.Tag?.Text);
    }

    private static string Describe(StrategyWire wire)
    {
        return wire.From.Block + "." + wire.From.Exit + "->" + wire.To;
    }
}