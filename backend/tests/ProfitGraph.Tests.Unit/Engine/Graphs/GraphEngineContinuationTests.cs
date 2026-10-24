using System.Globalization;
using System.Text.Json;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Templates;

using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Graphs;

public sealed class GraphEngineContinuationTests
{
    [TestCase("Sma", arg2: 5)]
    [TestCase("Sma", arg2: 100)]
    [TestCase("Sma", arg2: 289)]
    [TestCase("Ladder", arg2: 5)]
    [TestCase("Ladder", arg2: 6)]
    [TestCase("Ladder", arg2: 289)]
    public void RestoredTemplateContinuationMatchesUninterruptedRun(string template, int checkpointIndex)
    {
        Candle[] candles = LoadCandles();
        CompiledTradingGraph graph = Compile(template);
        (ExecutionSettings settings, InstrumentRules rules) = Settings();
        var original = new CausalDispatcher(graph, new ExternallyExecutedAccount(settings, rules), graphVersionId: "v1");
        var expected = new List<EventOccurrence>();

        for (var index = 0; index < 5; index++)
        {
            original.AdvanceClosedCandle(candles[index], index);
        }

        for (var index = 5; index <= checkpointIndex; index++)
        {
            Step(original, candles[index], index, expected);
        }

        GraphEngineCheckpoint checkpoint = original.CaptureCheckpoint("v1", candles[checkpointIndex].OpenTime.AddMinutes(5));

        if (template == "Ladder" && checkpointIndex is 5 or 6)
        {
            checkpoint.Account.Orders.Select(order => order.Tag).ShouldContain("ladder.average");
            checkpoint.Account.Orders.Select(order => order.Tag).ShouldContain("ladder.exit");
            checkpoint.Account.Orders.ShouldAllBe(order => order.Type == OrderType.Limit);
        }

        GraphEngineCheckpoint copy = JsonSerializer.Deserialize<GraphEngineCheckpoint>(JsonSerializer.Serialize(checkpoint)).ShouldNotBeNull();
        Candle[] history = candles[..(checkpointIndex + 1)];
        CausalDispatcher restored = CausalDispatcher.Restore(copy, graph, settings, rules, history);
        DispatchBatch resume = restored.Drain();
        resume.Occurrences.Select(occurrence => occurrence.Kind).ShouldBe(new[] { EventKind.Resume });
        resume.Occurrences[0].Payload.ShouldBe(new ResumePayload(ResumeReason.Restart, "v1", "v1"));
        restored.Started.ShouldBeTrue();
        JsonSerializer.Serialize(restored.Account.Capture()).ShouldBe(JsonSerializer.Serialize(original.Account.Capture()));
        restored.CaptureCheckpoint("v1", candles[checkpointIndex].OpenTime.AddMinutes(5)).Account.NextOrderId.ShouldBe(checkpoint.Account.NextOrderId);
        var actual = new List<EventOccurrence>();
        expected.Clear();

        for (int index = checkpointIndex + 1; index < candles.Length; index++)
        {
            Step(original, candles[index], index, expected);
            Step(restored, candles[index], index, actual);
        }

        actual.Count.ShouldBe(expected.Count);

        for (var index = 0; index < expected.Count; index++)
        {
            EventOccurrence baseline = expected[index];
            EventOccurrence resumed = actual[index];
            resumed.Id.ShouldBe(baseline.Id + 1);
            resumed.Sequence.ShouldBe(baseline.Sequence + 1);
            Shift(resumed.CausationId, checkpoint.NextEventId).ShouldBe(baseline.CausationId);
            resumed.Kind.ShouldBe(baseline.Kind);
            resumed.CandleIndex.ShouldBe(baseline.CandleIndex);
            resumed.CandleOpenTime.ShouldBe(baseline.CandleOpenTime);
            resumed.CandleCloseTime.ShouldBe(baseline.CandleCloseTime);
            resumed.Phase.ShouldBe(baseline.Phase);
            resumed.MarketPrice.ShouldBe(baseline.MarketPrice);
            PayloadSignature(resumed.Payload, checkpoint.NextEventId).ShouldBe(PayloadSignature(baseline.Payload, long.MaxValue));
        }

        JsonSerializer.Serialize(restored.Account.Capture()).ShouldBe(JsonSerializer.Serialize(original.Account.Capture()));
        JsonSerializer.Serialize(restored.CaptureCheckpoint("v1", candles[^1].OpenTime.AddMinutes(5)).Account)
            .ShouldBe(JsonSerializer.Serialize(original.CaptureCheckpoint("v1", candles[^1].OpenTime.AddMinutes(5)).Account));
        actual.ShouldNotContain(occurrence => occurrence.Kind == EventKind.Start);
    }

    private static void Step(CausalDispatcher dispatcher, Candle candle, int index, List<EventOccurrence> events)
    {
        DateTimeOffset closeTime = candle.OpenTime.AddMinutes(5);
        var open = new ReachedMarketContext(candle, index, closeTime, PricePhase.Open, candle.Open, index);

        if (!dispatcher.Started)
        {
            events.AddRange(dispatcher.StartAndDrain(open).Occurrences);
        }
        else
        {
            dispatcher.BeginOpen(open);
        }

        dispatcher.AdvanceClosedCandle(candle, index);
        var close = new ReachedMarketContext(candle, index, closeTime, PricePhase.Close, candle.Close, index + 1);
        dispatcher.BeginClose(close);
        dispatcher.EnqueueExternal(EventKind.CandleClosed, new CandlePayload(candle), close);
        events.AddRange(dispatcher.Drain().Occurrences);
        var external = new ReachedMarketContext(candle, index, closeTime, PricePhase.External, candle.Close, index + 1);

        for (var iteration = 0; iteration < 100; iteration++)
        {
            OrderSnapshot[] orders = dispatcher.Account.Capture().Orders.OrderBy(order => order.Id).ToArray();
            OrderSnapshot? pending = orders.FirstOrDefault(order => order.Status == OrderStatus.PendingSubmit);

            if (pending is not null)
            {
                Apply(ExternalOrderUpdate.Accept(pending.Id));

                continue;
            }

            pending = orders.FirstOrDefault(order => order.Status == OrderStatus.PendingCancel);

            if (pending is not null)
            {
                Apply(ExternalOrderUpdate.ConfirmCancel(pending.Id));

                continue;
            }

            pending = orders.FirstOrDefault(order => order.Status == OrderStatus.Active && order.EligibleFromCandleIndex <= index &&
                (order.Type == OrderType.Market || (order.Side == OrderSide.Buy && candle.Low <= order.LimitPrice) ||
                    (order.Side == OrderSide.Sell && candle.High >= order.LimitPrice)));

            if (pending is null)
            {
                return;
            }

            decimal price = pending.LimitPrice ?? candle.Close;
            decimal grossBase = pending.Quantity ?? decimal.Floor(pending.QuoteBudget!.Value / price * 100000000m) / 100000000m;
            Apply(ExternalOrderUpdate.Final(pending.Id, grossBase, grossBase * price, fee: 0m));
        }

        throw new InvalidOperationException(
            $"The deterministic external responder did not settle at candle {index}. Orders: {JsonSerializer.Serialize(dispatcher.Account.Capture().Orders)}. " +
            $"Diagnostics: {JsonSerializer.Serialize(dispatcher.ExternalOrderDiagnostics)}.");

        void Apply(ExternalOrderUpdate update)
        {
            dispatcher.ApplyExternalOrderUpdate(update, external);
            events.AddRange(dispatcher.Drain().Occurrences);
        }
    }

    private static long? Shift(long? id, long checkpointEventId)
    {
        return id > checkpointEventId ? id - 1 : id;
    }

    private static string PayloadSignature(EventPayload payload, long checkpointEventId)
    {
        if (payload is CancelResultPayload result)
        {
            return JsonSerializer.Serialize(
                new
                {
                    result.Cancelled,
                    result.CancelledOrderId,
                    result.OriginNodeId,
                    CauseId = Shift(result.Cause.Id, checkpointEventId),
                    result.Cause.Kind,
                    CausePayload = PayloadSignature(result.Cause.Payload, checkpointEventId)
                });
        }

        return JsonSerializer.Serialize(payload, payload.GetType());
    }

    private static CompiledTradingGraph Compile(string template)
    {
        StrategyDefinition definition = template switch
        {
            "Sma" => SmaStrategyBlockFactory.Create(fastPeriod: 2, slowPeriod: 5, buyAmount: 37m),
            "Ladder" => LadderStrategyBlockFactory.Create(buyAmount: 25m, allocationCap: 75m, drop: 0.005m, rise: 0.003m),
            _ => throw new ArgumentOutOfRangeException(nameof(template))
        };

        return new TradingGraphCompiler().Compile(new StrategyDefinitionLowerer(Catalog).Lower(definition).Graph, Catalog).Graph.ShouldNotBeNull();
    }

    private static (ExecutionSettings Settings, InstrumentRules Rules) Settings()
    {
        return (
            new ExecutionSettings(startingCapital: 1000m, slippage: 0.001m, limitFeeRate: 0.001m, marketFeeRate: 0.0015m),
            new InstrumentRules(quantityStep: 0.01m, priceTick: 0.00001m, minQuantity: 0.01m, minNotional: 1m, basePrecision: 8, quotePrecision: 8));
    }

    private static Candle[] LoadCandles()
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