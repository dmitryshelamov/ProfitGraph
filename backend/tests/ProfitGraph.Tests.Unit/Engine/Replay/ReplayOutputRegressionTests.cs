using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using NSubstitute;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Dispatch;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Metrics;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Unit.Engine.Fixtures;

using ValueType = ProfitGraph.Engine.Nodes.ValueType;

using static ProfitGraph.Tests.Unit.Engine.Graphs.ProductionTradingNodeTests;

namespace ProfitGraph.Tests.Unit.Engine.Replay;

public sealed class ReplayOutputRegressionTests
{
    // ADR 0057 added the limit fill mode, the open-position flag and the capital cost metrics; leaving them out of the hash shows
    // that everything recorded before them is byte-identical.
    private static readonly HashSet<(Type Type, string Name)> AddedLater = new HashSet<(Type Type, string Name)>
    {
        (typeof(ExecutionSettings), nameof(ExecutionSettings.LimitFill)),
        (typeof(CandleSnapshot), nameof(CandleSnapshot.InPosition)),
        (typeof(SampleMetrics), nameof(SampleMetrics.MaxCapitalDeployed)),
        (typeof(SampleMetrics), nameof(SampleMetrics.MaxCapitalDeployedPercent)),
        (typeof(SampleMetrics), nameof(SampleMetrics.MaxCapitalCommitted)),
        (typeof(SampleMetrics), nameof(SampleMetrics.TimeInPositionPercent)),
        (typeof(SampleMetrics), nameof(SampleMetrics.TimeUnderWaterPercent)),
        (typeof(SampleMetrics), nameof(SampleMetrics.LongestUnderWaterCandles))
    };

    private static readonly JsonSerializerOptions HashOptions = new JsonSerializerOptions
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                info =>
                {
                    for (int index = info.Properties.Count - 1; index >= 0; index--)
                    {
                        if (AddedLater.Contains((info.Type, info.Properties[index].Name)))
                        {
                            info.Properties.RemoveAt(index);
                        }
                    }
                }
            }
        }
    };

    [TestCase("Sma", "1833B6A171D5A84C6BF007EF2E26A05DD806C2CD1A9988F8CFCFED5970A0691C")]
    [TestCase("Ladder", "98963CB8B0F4B13191F1A0FBA9B176FAEEA31531BB7BBA16CCC59383E55AD075")]
    [TestCase("BlockSma", "1833B6A171D5A84C6BF007EF2E26A05DD806C2CD1A9988F8CFCFED5970A0691C")]
    [TestCase("BlockLadder", "98963CB8B0F4B13191F1A0FBA9B176FAEEA31531BB7BBA16CCC59383E55AD075")]
    [TestCase("BlockLadderResume", "98963CB8B0F4B13191F1A0FBA9B176FAEEA31531BB7BBA16CCC59383E55AD075")]
    public void FullReplayOutputMatchesPreW0(string workload, string expectedHash)
    {
        StrategyGraph graph = workload switch
        {
            "Sma" => SmaStrategyGraphGenerator.Generate(),
            "Ladder" => LadderStrategyGraphGenerator.Generate(),
            "BlockSma" => new StrategyDefinitionLowerer(Catalog).Lower(SmaStrategyBlockFactory.Create(fastPeriod: 2, slowPeriod: 3)).Graph,
            "BlockLadder" => new StrategyDefinitionLowerer(Catalog).Lower(LadderStrategyBlockFactory.Create()).Graph,
            "BlockLadderResume" => new StrategyDefinitionLowerer(Catalog).Lower(LadderWithResume()).Graph,
            _ => throw new ArgumentOutOfRangeException(nameof(workload))
        };
        CompiledTradingGraph compiled = new TradingGraphCompiler().Compile(graph, Catalog).Graph.ShouldNotBeNull();
        decimal[] prices = [4m, 3m, 2m, 5m, 6m, 2m, 1m, 4m, 6m, 2m];
        Candle[] candles = Enumerable.Range(start: 0, count: 128)
            .Select(index => workload.Contains("Ladder", StringComparison.Ordinal)
                ? new Candle(DateTimeOffset.UnixEpoch.AddHours(index), Open: 100m, High: 110m, Low: 85m, Close: 105m, Volume: 1m)
                : new Candle(
                    DateTimeOffset.UnixEpoch.AddHours(index),
                    prices[index % prices.Length],
                    prices[index % prices.Length],
                    prices[index % prices.Length],
                    prices[index % prices.Length],
                    Volume: 1m))
            .ToArray();
        var input = new BacktestInput(
            compiled,
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0m, limitFeeRate: 0.001m, marketFeeRate: 0.001m),
            new InstrumentRules(quantityStep: 0.000001m, priceTick: 0.001m, minQuantity: 0m, minNotional: 1m, basePrecision: 6, quotePrecision: 8),
            Timeframe.Parse("1h"),
            Array.Empty<Candle>(),
            candles,
            OutOfSampleSplit.Create(fraction: 0.25m));
        var observer = new MetricsObserver(input.Settings.StartingCapital, input.SampleRanges);
        BacktestResult result = new BacktestEngine().Run(input, observer);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        result.FillCount.ShouldBeGreaterThan(0);

        Hash(new { Result = result, Metrics = observer.Snapshot }).ShouldBe(expectedHash);
    }

    private static StrategyDefinition LadderWithResume()
    {
        StrategyDefinition pinned = LadderStrategyBlockFactory.Create();

        return new StrategyDefinition(
            pinned.Parameters,
            pinned.Values,
            [.. pinned.Blocks, new StrategyBlock("resume", "event", "При возобновлении", "OnResume")],
            [.. pinned.Wires, new StrategyWire(new StrategyExit("resume", "out"), "entryConditions")],
            pinned.TemplateKind);
    }

    [TestCase("LadderNearMinimum", "0D8A8ED2098CB0B6027DCA2EE0C17189D15B8B85805BEF621816CAC3BB350268")]
    [TestCase("LadderExhaustedCap", "ABE57DA4EFE970C594ECC1C39D29AC732D77662FA428121A233A9741687FEC86")]
    [TestCase("SmaShortPeriods", "E3DAE6B3C9173FD2F4B5344FDB5BE5F67CB42E63C187DE76AACE1B191DA40ABC")]
    [TestCase("LadderTagConflict", "C0CB94F2A1F5547B673FAED257A68912E32DC01B75EB777AE071FC10DCA46C69")]
    [TestCase("BlockAtrLadder", "F9DCB1BA884DFAA133D58F53B79210CDBC3820FAB764FD507D36C0C4CF6D63F8")]
    public void CompleteKasReplayOutputMatchesBeforeAccountSeam(string workload, string expectedHash)
    {
        BacktestInput input = KasInput(workload);
        var metrics = new MetricsObserver(input.Settings.StartingCapital, input.SampleRanges);
        var candles = new List<CandleSnapshot>();
        var occurrences = new List<EventOccurrence>();
        var observer = Substitute.For<IBacktestObserver>();
        observer.When(value => value.OnLedger(Arg.Any<BacktestLedgerEntry>()))
            .Do(call => metrics.OnLedger(call.Arg<BacktestLedgerEntry>()!));
        observer.When(value => value.OnCandleProcessed(Arg.Any<CandleSnapshot>()))
            .Do(call =>
            {
                var candle = call.Arg<CandleSnapshot>()!;
                candles.Add(candle);
                metrics.OnCandleProcessed(candle);
            });
        var options = new BacktestRunOptions { DispatchObserver = batch => occurrences.AddRange(batch.Occurrences) };
        BacktestResult result = new BacktestEngine().Run(input, observer, options: options);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        candles.Count.ShouldBe(input.Replay.Count);
        observer.Received(1).OnTerminal(result.Terminal);

        // Runtime payload serialization includes each concrete order/fill/rejection/cancellation field.
        var output = new
        {
            Result = result,
            Candles = candles,
            Occurrences = occurrences.Select(occurrence => new
            {
                Occurrence = occurrence,
                Payload = JsonSerializer.SerializeToElement(occurrence.Payload, occurrence.Payload.GetType())
            }),
            Metrics = metrics.Snapshot
        };

        switch (workload)
        {
            case "LadderNearMinimum":
                // The 1.00001 quote budget meets the 1 quote minimum; its normalized quantity is worth less and still fills.
                result.FillCount.ShouldBe(113);
                result.Ledger.ShouldNotContain(entry => entry.Rejection != null);
                result.Ledger.Count(entry => entry.Fill is { Tag: "ladder.entry" } && entry.Fill.GrossBase * entry.Fill.Price < 1m).ShouldBe(55);

                break;
            case "LadderExhaustedCap":
                // Two 25 quote buys exhaust the 50 quote cap; the run includes eight strategy cancellations.
                result.FillCount.ShouldBe(27);
                result.Ledger.Count(entry => entry.Kind == BacktestLedgerKind.Cancelled).ShouldBe(8);
                candles.Any(candle => candle.Account.CostBasis > 49m && candle.Account.Orders.All(order => order.Tag != "ladder.average"))
                    .ShouldBeTrue();

                break;
            case "SmaShortPeriods":
                // Periods 2/5 produce 223 completed exits; seven residual-dust attempts reject ZeroQuantity.
                result.Ledger.Count(entry => entry.Fill?.Side == OrderSide.Sell).ShouldBe(223);
                result.Ledger.Count(entry => entry.Rejection?.Reason == RejectionReason.ZeroQuantity).ShouldBe(7);

                break;
            case "LadderTagConflict":
                result.Ledger.Count(entry => entry.Rejection?.Reason == RejectionReason.TagActive).ShouldBe(55);

                break;
            case "BlockAtrLadder":
                result.FillCount.ShouldBe(484);
                result.Ledger.Count(entry => entry.Fill?.Side == OrderSide.Sell).ShouldBe(191);

                break;
        }

        string hash = Hash(output);
        TestContext.Progress.WriteLine(
            $"{workload}: hash={hash}; fills={result.FillCount}; orders={result.OrderCount}; " +
            $"cancellations={result.Ledger.Count(entry => entry.Kind == BacktestLedgerKind.Cancelled)}; " +
            $"rejections={result.Ledger.Count(entry => entry.Rejection is not null)}; " +
            $"cost_basis={result.Terminal.Account.CostBasis}; occurrences={occurrences.Count}");
        hash.ShouldBe(expectedHash);
    }

    [TestCase("LadderExhaustedCap", "CB1B1A1F63A9BE52CEE2AA950D5F93C3F4A7751CF08DCB6DE25D98AC19ED2C76")]
    [TestCase("SmaShortPeriods", "DDC1163A4D36E6F45CA12E2D04B0096D36779A8A3CD39CC96AB1D0067E00E31E")]
    public void PreparedMetricsOnlyTrialMatchesBeforeAccountSeam(string workload, string expectedHash)
    {
        BacktestInput baseline = KasInput(workload);
        var prepared = new PreparedReplay(baseline.Timeframe, baseline.Warmup, baseline.Replay);
        PreparedReplay inSample = prepared.Slice(baseline.SampleRanges.InSampleBars);
        var input = new BacktestInput(baseline.Graph, baseline.Selections, baseline.Settings, baseline.Rules, inSample);
        var observer = new MetricsObserver(input.Settings.StartingCapital, input.SampleRanges);
        BacktestResult result = new BacktestEngine().Run(input, observer, options: new BacktestRunOptions { RetainLedger = false });
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        result.Ledger.ShouldBeEmpty();
        result.FillCount.ShouldBeGreaterThan(0);
        observer.Snapshot.Full.ObservedCandles.ShouldBe(inSample.Replay.Count);
        string hash = Hash(observer.Snapshot);
        TestContext.Progress.WriteLine($"{workload} trial: hash={hash}; bars={inSample.Replay.Count}; fills={result.FillCount}");
        hash.ShouldBe(expectedHash);
    }

    [Test]
    public void ProjectedLadderExhaustedCapOutputMatchesBeforeAccountSeam()
    {
        BacktestInput input = KasInput("LadderExhaustedCap");
        var metrics = new MetricsObserver(input.Settings.StartingCapital, input.SampleRanges);
        var candles = new List<CandleSnapshot>();
        var batches = new List<DispatchBatch>();
        var observer = Substitute.For<IBacktestObserver>();
        observer.When(value => value.OnLedger(Arg.Any<BacktestLedgerEntry>()))
            .Do(call => metrics.OnLedger(call.Arg<BacktestLedgerEntry>()!));
        observer.When(value => value.OnCandleProcessed(Arg.Any<CandleSnapshot>()))
            .Do(call =>
            {
                var candle = call.Arg<CandleSnapshot>()!;
                candles.Add(candle);
                metrics.OnCandleProcessed(candle);
            });
        var options = new BacktestRunOptions { RetainDispatchProjections = true, DispatchObserver = batches.Add };
        BacktestResult result = new BacktestEngine().Run(input, observer, options: options);
        result.Terminal.Outcome.ShouldBe(BacktestOutcome.Completed);
        result.FillCount.ShouldBe(27);
        result.Ledger.Count(entry => entry.Kind == BacktestLedgerKind.Cancelled).ShouldBe(8);
        candles.Count.ShouldBe(input.Replay.Count);
        observer.Received(1).OnTerminal(result.Terminal);
        batches.SelectMany(batch => batch.Observations).ShouldNotBeEmpty();
        batches.SelectMany(batch => batch.AppliedActions).ShouldNotBeEmpty();

        // Serialize every copied eager projection, including concrete polymorphic payloads and typed outputs.
        var output = new
        {
            Result = result,
            Candles = candles,
            Metrics = metrics.Snapshot,
            Batches = batches.Select(batch => new
            {
                Observations = batch.Observations.Select(observation => new
                {
                    Occurrence = OccurrenceProjection(observation.Occurrence),
                    observation.Account,
                    Outputs = observation.Outputs.OrderBy(pair => pair.Key.NodeId, StringComparer.Ordinal)
                        .ThenBy(pair => pair.Key.PortKey, StringComparer.Ordinal)
                        .Select(pair => new
                        {
                            Port = pair.Key,
                            pair.Value.ValueType,
                            pair.Value.IsAvailable,
                            Value = EngineValueProjection(pair.Value)
                        }),
                    Requests = observation.Requests.Select(request => JsonSerializer.SerializeToElement(request, request.GetType()))
                }),
                batch.Skips,
                AppliedActions = batch.AppliedActions.Select(action => new
                {
                    action.NodeId,
                    action.CauseId,
                    action.AttemptId,
                    Results = action.Results.Select(emission => new
                    {
                        emission.Kind,
                        Payload = JsonSerializer.SerializeToElement(emission.Payload, emission.Payload.GetType())
                    })
                }),
                Occurrences = batch.Occurrences.Select(OccurrenceProjection)
            })
        };
        string hash = Hash(output);
        TestContext.Progress.WriteLine($"Projected LadderExhaustedCap: hash={hash}; batches={batches.Count}");
        hash.ShouldBe("C8B8FE772A3ABC5C4589D0573CDBBF6295D0E29A1DC5107AAF92089977188642");
    }

    private static object? EngineValueProjection(EngineValue value)
    {
        if (!value.IsAvailable)
        {
            return null;
        }

        return value.ValueType switch
        {
            ValueType.Integer or ValueType.Scalar or ValueType.Price or ValueType.BaseAmount or ValueType.QuoteAmount => value.Number,
            ValueType.Boolean => value.Boolean,
            ValueType.Candle => value.Candle,
            ValueType.Tag => value.Tag,
            ValueType.OrderId => value.OrderId,
            ValueType.Order => value.Order,
            ValueType.Event => OccurrenceProjection(value.Event),
            ValueType.OrderSide => value.Side,
            ValueType.OrderType => value.Type,
            ValueType.RejectionReason => value.Reason,
            _ => throw new ArgumentOutOfRangeException(nameof(value))
        };
    }

    private static object OccurrenceProjection(EventOccurrence occurrence)
    {
        return new
        {
            Occurrence = occurrence,
            Payload = JsonSerializer.SerializeToElement(occurrence.Payload, occurrence.Payload.GetType())
        };
    }

    private static BacktestInput KasInput(string workload)
    {
        StrategyDefinition definition = workload switch
        {
            "LadderNearMinimum" => LadderStrategyBlockFactory.Create(buyAmount: 1.00001m, allocationCap: 3.00003m, drop: 0.005m, rise: 0.003m),
            "LadderExhaustedCap" => LadderStrategyBlockFactory.Create(buyAmount: 25m, allocationCap: 50m, drop: 0.005m, rise: 0.03m),
            "SmaShortPeriods" => SmaStrategyBlockFactory.Create(fastPeriod: 2, slowPeriod: 5, buyAmount: 37m),
            "LadderTagConflict" => LadderStrategyBlockFactory.Create(buyAmount: 25m, allocationCap: 75m, drop: 0.005m, rise: 0.003m),
            "BlockAtrLadder" => AtrLadderStrategyDefinitionGenerator.Generate(),
            _ => throw new ArgumentOutOfRangeException(nameof(workload))
        };

        if (workload == "BlockAtrLadder")
        {
            definition = new StrategyDefinition(
                definition.Parameters.Select(parameter => parameter.Name switch
                {
                    "buyAmount" => parameter with { DefaultValue = 25m },
                    "allocationCap" => parameter with { DefaultValue = 75m },
                    "drop" => parameter with { DefaultValue = 0.005m },
                    "atrPeriod" => parameter with { DefaultValue = 2m },
                    "k" => parameter with { DefaultValue = 0.1m },
                    _ => parameter
                }),
                definition.Values,
                definition.Blocks,
                definition.Wires,
                definition.TemplateKind);
        }

        if (workload == "LadderTagConflict")
        {
            // The template guards duplicate tags; alias only the averaging order's tag to reach TagActive.
            definition = new StrategyDefinition(
                definition.Parameters,
                definition.Values,
                definition.Blocks.Select(block => block.Id == "average"
                    ? new StrategyBlock(
                        block.Id,
                        block.Kind,
                        block.DisplayName,
                        orderKind: block.OrderKind,
                        side: block.Side,
                        tag: new ValueExpression("constant", block.Tag!.ValueType, text: "ladder.exit"),
                        amount: block.Amount,
                        price: block.Price)
                    : block),
                definition.Wires,
                definition.TemplateKind);
        }

        StrategyGraph graph = new StrategyDefinitionLowerer(Catalog).Lower(definition).Graph;
        CompiledTradingGraph compiled = new TradingGraphCompiler().Compile(graph, Catalog).Graph.ShouldNotBeNull();
        Candle[] candles = File.ReadLines(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "MarketData", "kasusdt-5m-20260901-20260908.csv"))
            .Where(line => !line.StartsWith('#'))
            .Skip(1)
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

        return new BacktestInput(
            compiled,
            EngineExecutionInputs.Empty,
            new ExecutionSettings(startingCapital: 1000m, slippage: 0.001m, limitFeeRate: 0.001m, marketFeeRate: 0.0015m),
            new InstrumentRules(quantityStep: 0.01m, priceTick: 0.00001m, minQuantity: 0.01m, minNotional: 1m, basePrecision: 8, quotePrecision: 8),
            Timeframe.Parse("5m"),
            candles.Take(5),
            candles.Skip(5),
            OutOfSampleSplit.Create(fraction: 0.25m));
    }

    private static string Hash<T>(T output)
    {
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(output, HashOptions)));
    }
}