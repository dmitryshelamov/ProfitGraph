using ProfitGraph.Application.Backtesting;
using ProfitGraph.Application.Bots;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Unit.Application.Strategies;
using ProfitGraph.Tests.Unit.Engine.Graphs;

namespace ProfitGraph.Tests.Unit.Application.Bots;

/// <summary>
///     ADR 0058, sections 8 and 9: a grid bot needs a venue that states its room for resting orders, room for the grid's buys
///     and sales, and fees its profit step pays; a strategy without a grid needs none of it.
/// </summary>
public sealed class BotGridRulesTests
{
    private static readonly BacktestCompiler Compiler = new BacktestCompiler(new TestNodeTypeRegistry(ProductionTradingNodeTests.Catalog));
    private static readonly CandleSeries Series = new CandleSeries(new VenueId("venue"), new InstrumentSymbol("BTCUSDT"), Timeframe.Parse("1h"));
    private static readonly BotFeeRates Rates = new BotFeeRates(limitRate: 0.001m, marketRate: 0.001m, DateTimeOffset.UnixEpoch);

    [Test]
    public void AStrategyWithoutAGridNeedsNoCapacity()
    {
        Should.NotThrow(() => Rules(capacity: null).Ensure(Series, Rates, Plan(SmaStrategyBlockFactory.Create())));
    }

    [TestCase(arg1: null, BotErrorCodes.GridNotSupportedByVenue)]
    [TestCase(arg1: 19, BotErrorCodes.GridExceedsVenueOrders)]
    public void AVenueWithoutRoomRefusesTheGrid(int? capacity, string code)
    {
        var refused = Should.Throw<ApplicationValidationException>(() =>
            Rules(capacity).Ensure(Series, Rates, Plan(GridStrategyBlockFactory.Create(levels: 10, maxOpenBuys: 10))));

        refused.Errors.ShouldHaveSingleItem().Code.ShouldBe(code);
    }

    [Test]
    public void AProfitStepTheFeesEatIsRefused()
    {
        var refused = Should.Throw<ApplicationValidationException>(() =>
            Rules(capacity: 20).Ensure(Series, Rates, Plan(GridStrategyBlockFactory.Create(profitStep: 0.002m))));

        refused.Errors.ShouldHaveSingleItem().Code.ShouldBe(BotErrorCodes.GridProfitBelowFees);
    }

    [Test]
    public void AGridThatFitsAndPaysItsFeesIsAccepted()
    {
        Should.NotThrow(() => Rules(capacity: 20).Ensure(Series, Rates, Plan(GridStrategyBlockFactory.Create())));
    }

    private static BotGridRules Rules(int? capacity)
    {
        return new BotGridRules(new VenuePorts<IVenueRestingOrderCapacity>(capacity is { } limit ? [new FixedRestingOrderCapacity(Series.Venue, limit)] : []));
    }

    private static CompiledTradingGraph Plan(StrategyDefinition definition)
    {
        return Compiler.Compile(definition, new Dictionary<string, decimal>());
    }
}