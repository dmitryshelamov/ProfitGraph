using System.Globalization;
using System.Text.Json;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Engine.Templates;

using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class SynchronousAccountParityTests
{
    [TestCase("Sma")]
    [TestCase("Ladder")]
    public void SynchronousFakeMatchesTradingAccountOnFirstThreeHundredKasCandles(string template)
    {
        StrategyDefinition definition = template switch
        {
            "Sma" => SmaStrategyBlockFactory.Create(fastPeriod: 2, slowPeriod: 5, buyAmount: 37m),
            "Ladder" => LadderStrategyBlockFactory.Create(buyAmount: 25m, allocationCap: 75m, drop: 0.005m, rise: 0.003m),
            _ => throw new ArgumentOutOfRangeException(nameof(template))
        };
        StrategyGraph graph = new StrategyDefinitionLowerer(Catalog).Lower(definition).Graph;
        CompiledTradingGraph compiled = new TradingGraphCompiler().Compile(graph, Catalog).Graph.ShouldNotBeNull();
        Candle[] candles = LoadFirstThreeHundredCandles();
        var settings = new ExecutionSettings(startingCapital: 1000m, slippage: 0.001m, limitFeeRate: 0.001m, marketFeeRate: 0.0015m);
        var rules = new InstrumentRules(quantityStep: 0.01m, priceTick: 0.00001m, minQuantity: 0.01m, minNotional: 1m, basePrecision: 8, quotePrecision: 8);
        var baseline = new TradingAccount(settings, rules);
        var fake = new ScriptedSynchronousAccount(settings, rules);

        (string[] expectedEvents, AccountSnapshot expectedAccount, int expectedFills) = Replay(compiled, baseline, baseline, candles);
        (string[] actualEvents, AccountSnapshot actualAccount, int actualFills) = Replay(compiled, fake, fake.Inner, candles);

        actualEvents.ShouldBe(expectedEvents);
        JsonSerializer.Serialize(actualAccount).ShouldBe(JsonSerializer.Serialize(expectedAccount));
        actualFills.ShouldBe(expectedFills);
        actualFills.ShouldBeGreaterThan(0);
        fake.Submissions.ShouldNotBeEmpty();

        if (template == "Ladder")
        {
            fake.CancelledTags.ShouldNotBeEmpty();
        }
    }

    private static (string[] Events, AccountSnapshot Account, int Fills) Replay(
        CompiledTradingGraph graph,
        IGraphAccount graphAccount,
        TradingAccount executionAccount,
        Candle[] candles)
    {
        var dispatcher = new CausalDispatcher(graph, graphAccount);
        var events = new List<string>();
        var fills = 0;

        for (var index = 0; index < 5; index++)
        {
            dispatcher.AdvanceClosedCandle(candles[index], index);
        }

        for (var index = 0; index < candles.Length - 5; index++)
        {
            Candle candle = candles[index + 5];
            ReachedMarketContext open = Context(candle, index, PricePhase.Open, candle.Open);
            dispatcher.BeginOpen(open);

            if (index == 0)
            {
                dispatcher.EnqueueExternal(EventKind.Start, new StartPayload(), open);
                Drain();
            }

            Match(open);

            foreach (PriceSegment segment in PricePath.Build(candle).Segments)
            {
                decimal current = segment.Start;
                dispatcher.AdvancePrice(current);

                while (OrderMatcher.Next(segment, current, executionAccount, index) is { } candidate)
                {
                    current = candidate.PathPrice;
                    dispatcher.AdvancePrice(current);
                    Match(Context(candle, index, segment.Phase, current));
                }

                dispatcher.AdvancePrice(segment.End);
            }

            dispatcher.AdvanceClosedCandle(candle, index + 5);
            ReachedMarketContext close = Context(candle, index, PricePhase.Close, candle.Close);
            dispatcher.BeginClose(close);
            dispatcher.EnqueueExternal(EventKind.CandleClosed, new CandlePayload(candle), close);
            Drain();
        }

        executionAccount.ReleaseAll();

        return (events.ToArray(), graphAccount.Capture(), fills);

        void Match(ReachedMarketContext context)
        {
            while (true)
            {
                Order? market = executionAccount.Orders.Active
                    .Where(order => order.Type == OrderType.Market && order.IsEligibleAt(context.CandleIndex))
                    .OrderBy(order => order.Id)
                    .FirstOrDefault();
                long? orderId = market?.Id ?? OrderMatcher.NextAtPrice(context.MarketPrice, executionAccount, context.CandleIndex)?.OrderId;

                if (orderId is null)
                {
                    return;
                }

                Order order = executionAccount.Orders.Get(orderId.Value)!;
                OrderSnapshot original = OrderSnapshot.Capture(order);
                OrderExecution execution = executionAccount.Execute(orderId.Value, context.MarketPrice);
                dispatcher.PublishExecution(original, execution, context);
                Drain();
            }
        }

        void Drain()
        {
            DispatchBatch batch = dispatcher.DrainForReplay(CancellationToken.None);
            fills += batch.Occurrences.Count(occurrence => occurrence.Kind == EventKind.Filled);
            events.AddRange(
                batch.Occurrences.Select(occurrence => JsonSerializer.Serialize(
                    new
                    {
                        Occurrence = occurrence,
                        Payload = JsonSerializer.SerializeToElement(occurrence.Payload, occurrence.Payload.GetType())
                    })));
        }
    }

    private static ReachedMarketContext Context(Candle candle, int index, PricePhase phase, decimal price)
    {
        return new ReachedMarketContext(
            candle,
            index,
            candle.OpenTime.AddMinutes(5),
            phase,
            price,
            phase == PricePhase.Close ? index + 1 : index);
    }

    private static Candle[] LoadFirstThreeHundredCandles()
    {
        return File.ReadLines(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "MarketData", "kasusdt-5m-20260901-20260908.csv"))
            .Where(line => !line.StartsWith('#'))
            .Skip(1)
            .Take(300)
            .Select(line =>
            {
                string[] cells = line.Split(',');

                return new Candle(
                    DateTimeOffset.Parse(cells[0], CultureInfo.InvariantCulture),
                    decimal.Parse(cells[1], CultureInfo.InvariantCulture),
                    decimal.Parse(cells[2], CultureInfo.InvariantCulture),
                    decimal.Parse(cells[3], CultureInfo.InvariantCulture),
                    decimal.Parse(cells[4], CultureInfo.InvariantCulture),
                    decimal.Parse(cells[5], CultureInfo.InvariantCulture));
            })
            .ToArray();
    }
}