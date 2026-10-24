using ProfitGraph.Domain.Optimization;

namespace ProfitGraph.Tests.Unit.Domain.Optimization;

public sealed class OptimizationSeedsTests
{
    [TestCase(arg1: 0UL, arg2: 0, arg3: 0xE220A8397B1DCDAFUL)]
    [TestCase(arg1: 0UL, arg2: 1, arg3: 0x6E789E6AA1B965F4UL)]
    [TestCase(arg1: 0UL, arg2: 2, arg3: 0x06C45D188009454FUL)]
    [TestCase(arg1: 42UL, arg2: 0, arg3: 0xBDD732262FEB6E95UL)]
    public void WindowSeedMatchesSplitMix64KnownWord(ulong seed, int windowIndex, ulong expected)
    {
        OptimizationSeeds.Derive(seed, windowIndex).ShouldBe(expected);
    }

    [Test]
    public void WindowSeedsAreStableDistinctAndIndependentOfRequestOrder()
    {
        ulong[] seeds = Enumerable.Range(start: 0, count: 50)
            .Select(index => OptimizationSeeds.Derive(ulong.MaxValue, index))
            .ToArray();
        seeds.Distinct().Count().ShouldBe(50);

        foreach (int index in Enumerable.Range(start: 0, count: 50).Reverse())
        {
            OptimizationSeeds.Derive(ulong.MaxValue, index).ShouldBe(seeds[index]);
        }
    }

    [Test]
    public void NegativeWindowIndexIsRejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => OptimizationSeeds.Derive(seed: 0UL, index: -1));
    }

    [Test]
    public void MaximumWindowIndexDoesNotOverflowSignedArithmetic()
    {
        OptimizationSeeds.Derive(seed: 0UL, int.MaxValue).ShouldBe(0xC051D9EB6C8FAD4EUL);
    }
}