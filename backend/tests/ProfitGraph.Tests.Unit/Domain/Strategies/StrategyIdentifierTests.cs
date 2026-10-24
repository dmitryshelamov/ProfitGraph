using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Tests.Unit.Domain.Strategies;

public sealed class StrategyIdentifierTests
{
    [Test]
    public void EmptyIdentifiersAreRejected()
    {
        Should.Throw<ArgumentException>(() => new StrategyId(Guid.Empty));
        Should.Throw<ArgumentException>(() => new StrategyVersionId(Guid.Empty));
        Should.Throw<ArgumentException>(() => new UserId(Guid.Empty));
    }

    [Test]
    public void IdentifiersCompareByValue()
    {
        var value = Guid.NewGuid();

        new StrategyId(value).ShouldBe(new StrategyId(value));
        StrategyId.New().ShouldNotBe(StrategyId.New());
        new StrategyId(value).ToString().ShouldBe(value.ToString());
    }
}