using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Tests.Unit.Domain.Strategies;

public sealed class StrategyNameTests
{
    [Test]
    public void NameIsTrimmedAndComparedByValue()
    {
        var name = new StrategyName("  SMA  ");

        name.Value.ShouldBe("SMA");
        name.ShouldBe(new StrategyName("SMA"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void EmptyNameIsRejected(string? value)
    {
        Should.Throw<StrategyRuleException>(() => new StrategyName(value)).Code.ShouldBe(StrategyRuleException.InvalidName);
    }

    [Test]
    public void NameFitsTheLimit()
    {
        new StrategyName(new string(c: 'a', StrategyName.MaxLength)).Value.Length.ShouldBe(StrategyName.MaxLength);
        Should.Throw<StrategyRuleException>(() => new StrategyName(new string(c: 'a', StrategyName.MaxLength + 1)));
    }
}