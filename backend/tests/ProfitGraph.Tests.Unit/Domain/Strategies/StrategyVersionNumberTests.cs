using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Tests.Unit.Domain.Strategies;

public sealed class StrategyVersionNumberTests
{
    [Test]
    public void NumbersStartAtOneAndGrowByOne()
    {
        StrategyVersionNumber.First.Value.ShouldBe(1);
        StrategyVersionNumber.First.Next().ShouldBe(new StrategyVersionNumber(2));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void NumberBelowOneIsRejected(int value)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new StrategyVersionNumber(value));
    }

    [Test]
    public void NextNumberDoesNotOverflow()
    {
        Should.Throw<OverflowException>(() => new StrategyVersionNumber(int.MaxValue).Next());
    }
}