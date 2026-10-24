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

    [Test]
    public void CreateTrimsTheNameAndStartsAtVersionOne()
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();

        (Strategy strategy, StrategyVersion version) = Strategy.Create(id, owner, "  SMA  ", Definition, Layout, Created);

        strategy.ShouldSatisfyAllConditions(
            () => strategy.Name.ShouldBe("SMA"),
            () => strategy.OwnerId.ShouldBe(owner),
            () => strategy.CurrentVersion.ShouldBe(1),
            () => strategy.CreatedAt.ShouldBe(Created),
            () => strategy.UpdatedAt.ShouldBe(Created));
        version.ShouldSatisfyAllConditions(
            () => version.StrategyId.ShouldBe(id),
            () => version.Number.ShouldBe(1),
            () => version.Definition.ShouldBe(Definition),
            () => version.CreatedAt.ShouldBe(Created));
    }

    [Test]
    public void CreateRequiresAnIdentifierAndAnOwner()
    {
        Should.Throw<ArgumentException>(() => Strategy.Create(Guid.Empty, Guid.NewGuid(), "SMA", Definition, Layout, Created));
        Should.Throw<ArgumentException>(() => Strategy.Create(Guid.NewGuid(), Guid.Empty, "SMA", Definition, Layout, Created));
    }

    [TestCase("")]
    [TestCase("   ")]
    public void CreateRejectsAnEmptyName(string name)
    {
        Should.Throw<StrategyRuleException>(() => Strategy.Create(Guid.NewGuid(), Guid.NewGuid(), name, Definition, Layout, Created))
            .Code.ShouldBe(StrategyRuleException.InvalidName);
    }

    [Test]
    public void CreateRejectsATooLongName()
    {
        Should.Throw<StrategyRuleException>(() => Strategy.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string(c: 'a', Strategy.MaxNameLength + 1),
            Definition,
            Layout,
            Created));
    }

    [Test]
    public void SaveRecordsTheNextVersionAndKeepsCreationTime()
    {
        Strategy strategy = Create();

        StrategyVersion second = strategy.Save(" Renamed ", Definition, Layout, Later);
        StrategyVersion third = strategy.Save("Renamed", Definition, Layout, Later.AddHours(1));

        second.Number.ShouldBe(2);
        third.Number.ShouldBe(3);
        second.Id.ShouldNotBe(third.Id);
        strategy.ShouldSatisfyAllConditions(
            () => strategy.Name.ShouldBe("Renamed"),
            () => strategy.CurrentVersion.ShouldBe(3),
            () => strategy.CreatedAt.ShouldBe(Created),
            () => strategy.UpdatedAt.ShouldBe(Later.AddHours(1)));
    }

    [Test]
    public void SaveWithAnInvalidNameLeavesTheStrategyUnchanged()
    {
        Strategy strategy = Create();

        Should.Throw<StrategyRuleException>(() => strategy.Save(" ", Definition, Layout, Later));

        strategy.ShouldSatisfyAllConditions(
            () => strategy.Name.ShouldBe("SMA"),
            () => strategy.CurrentVersion.ShouldBe(1),
            () => strategy.UpdatedAt.ShouldBe(Created));
    }

    [Test]
    public void OnlyTheOwnerOwnsTheStrategy()
    {
        var owner = Guid.NewGuid();
        Strategy strategy = Strategy.Restore(Guid.NewGuid(), owner, "SMA", currentVersion: 1, Created, Created);

        strategy.IsOwnedBy(owner).ShouldBeTrue();
        strategy.IsOwnedBy(Guid.NewGuid()).ShouldBeFalse();
    }

    [Test]
    public void RestoreRejectsInvalidStoredState()
    {
        Should.Throw<ArgumentException>(() => Strategy.Restore(Guid.NewGuid(), Guid.Empty, "SMA", currentVersion: 1, Created, Created));
        Should.Throw<ArgumentException>(() => Strategy.Restore(Guid.NewGuid(), Guid.NewGuid(), "SMA", currentVersion: 0, Created, Created));
    }

    private static Strategy Create()
    {
        return Strategy.Create(Guid.NewGuid(), Guid.NewGuid(), "SMA", Definition, Layout, Created).Strategy;
    }
}