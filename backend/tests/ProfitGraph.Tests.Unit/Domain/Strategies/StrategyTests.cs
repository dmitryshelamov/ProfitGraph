using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Templates;

namespace ProfitGraph.Tests.Unit.Domain.Strategies;

public sealed class StrategyTests
{
    private static readonly DateTimeOffset Created = new DateTimeOffset(year: 2026, month: 10, day: 24, hour: 10, minute: 0, second: 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Created.AddHours(1);
    private static readonly StrategyDefinition Definition = SmaStrategyBlockFactory.Create();
    private static readonly GraphLayout Layout = new GraphLayout(new Dictionary<string, NodePosition>());
    private static readonly StrategyName Sma = new StrategyName("SMA");

    [Test]
    public void CreateStartsAtTheFirstVersion()
    {
        var id = StrategyId.New();
        var owner = UserId.New();

        (Strategy strategy, StrategyVersion version) = Strategy.Create(id, owner, Sma, Definition, Layout, Created);

        strategy.ShouldSatisfyAllConditions(
            () => strategy.Id.ShouldBe(id),
            () => strategy.Name.ShouldBe(Sma),
            () => strategy.OwnerId.ShouldBe(owner),
            () => strategy.CurrentVersion.ShouldBe(StrategyVersionNumber.First),
            () => strategy.CreatedAt.ShouldBe(Created),
            () => strategy.UpdatedAt.ShouldBe(Created));
        version.ShouldSatisfyAllConditions(
            () => version.StrategyId.ShouldBe(id),
            () => version.Number.ShouldBe(StrategyVersionNumber.First),
            () => version.Definition.ShouldBe(Definition),
            () => version.CreatedAt.ShouldBe(Created));
    }

    [Test]
    public void SaveRecordsTheNextVersionAndKeepsCreationTime()
    {
        Strategy strategy = Create();
        var renamed = new StrategyName("Renamed");

        StrategyVersion second = strategy.Save(renamed, Definition, Layout, Later);
        StrategyVersion third = strategy.Save(renamed, Definition, Layout, Later.AddHours(1));

        second.Number.ShouldBe(new StrategyVersionNumber(2));
        third.Number.ShouldBe(new StrategyVersionNumber(3));
        second.Id.ShouldNotBe(third.Id);
        strategy.ShouldSatisfyAllConditions(
            () => strategy.Name.ShouldBe(renamed),
            () => strategy.CurrentVersion.ShouldBe(third.Number),
            () => strategy.CreatedAt.ShouldBe(Created),
            () => strategy.UpdatedAt.ShouldBe(Later.AddHours(1)));
    }

    [Test]
    public void OnlyTheOwnerOwnsTheStrategy()
    {
        var owner = UserId.New();
        Strategy strategy = Strategy.Restore(StrategyId.New(), owner, Sma, StrategyVersionNumber.First, Created, Created);

        strategy.IsOwnedBy(owner).ShouldBeTrue();
        strategy.IsOwnedBy(UserId.New()).ShouldBeFalse();
    }

    [Test]
    public void RestoreKeepsTheStoredState()
    {
        var id = StrategyId.New();
        var version = new StrategyVersionNumber(7);

        Strategy strategy = Strategy.Restore(id, UserId.New(), Sma, version, Created, Later);

        strategy.ShouldSatisfyAllConditions(
            () => strategy.Id.ShouldBe(id),
            () => strategy.CurrentVersion.ShouldBe(version),
            () => strategy.UpdatedAt.ShouldBe(Later));
    }

    private static Strategy Create()
    {
        return Strategy.Create(StrategyId.New(), UserId.New(), Sma, Definition, Layout, Created).Strategy;
    }
}