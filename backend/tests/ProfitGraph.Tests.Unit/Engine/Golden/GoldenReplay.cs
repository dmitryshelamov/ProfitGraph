using System.Globalization;
using System.Text;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Graphs.Composites;
using ProfitGraph.Engine.Lowering;
using ProfitGraph.Engine.Market;
using ProfitGraph.Engine.Nodes;
using ProfitGraph.Engine.Nodes.BuiltIn;
using ProfitGraph.Engine.Replay;
using ProfitGraph.Engine.Templates;

namespace ProfitGraph.Tests.Unit.Engine.Golden;

/// <summary>
///     Golden replay scenarios: strategy templates on recorded public candles with fixed execution settings.
///     The same scenarios and the same canonical text are produced by the archived engine and by ProfitGraph.
/// </summary>
internal static class GoldenReplay
{
    private static readonly ExecutionSettings Settings = new ExecutionSettings(startingCapital: 1000m, slippage: 0.0005m, limitFeeRate: 0.001m, marketFeeRate: 0.001m);

    private static readonly IReadOnlyList<INodeDefinition> Catalog = GraphEngineNodeCatalog.WithResume(
            new INodeDefinition[]
            {
                new ConstantNodeDefinition(), new ParameterNodeDefinition(), new CandleFieldNodeDefinition(), new DataSourceNodeDefinition(), new SmaNodeDefinition(),
                new AtrNodeDefinition(), new PreviousCandleValueNodeDefinition(), new ArithmeticNodeDefinition(), new CompareNodeDefinition(),
                new CrossesAboveNodeDefinition(), new CrossesBelowNodeDefinition(), new AndNodeDefinition(), new OrNodeDefinition(), new NotNodeDefinition(),
                new IsAvailableNodeDefinition(), new OnStartNodeDefinition(), new OnCandleClosedNodeDefinition(),
                new MergeEventsNodeDefinition(),
                new FilterEventNodeDefinition(), new DecisionNodeDefinition()
            }.Concat(BuiltInComposites.Definitions))
        .Concat(Enum.GetValues<TradingNodeBehavior>().Select(behavior => new TradingNodeDefinition(behavior)))
        .ToArray();

    public static IReadOnlyList<string> Templates { get; } =
    [
        "sma", "ladder", "ladder-tight", "ladder-plus", "ladder-sma", "ladder-resume", "profit-fox-dca", "profit-fox-ladder", "capitulation-dip",
        "volume-momentum"
    ];

    public static IReadOnlyList<string> Datasets { get; } = ["kasusdt-4h", "wifusdt-4h", "kasusdt-5m"];

    public static string Run(string template, string dataset, string marketDataDirectory)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        try
        {
            Candle[] candles = ReadCandles(Path.Combine(marketDataDirectory, FileName(dataset)));
            var warmup = 200;
            var input = new BacktestInput(
                Compile(Definition(template)),
                EngineExecutionInputs.Empty,
                Settings,
                Rules(dataset),
                Timeframe.Parse(dataset.EndsWith("-5m", StringComparison.Ordinal) ? "5m" : "4h"),
                candles.Take(warmup),
                candles.Skip(warmup));

            return Describe(new BacktestEngine().Run(input));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private static StrategyDefinition Definition(string template)
    {
        return template switch
        {
            "sma" => SmaStrategyBlockFactory.Create(),
            "ladder" => LadderStrategyBlockFactory.Create(),
            "ladder-tight" => LadderStrategyBlockFactory.Create(buyAmount: 50m, allocationCap: 400m, drop: 0.03m, rise: 0.03m),
            "ladder-plus" => LadderPlusStrategyBlockFactory.Create(),
            "ladder-sma" => LadderSmaStrategyBlockFactory.Create(),
            "ladder-resume" => LadderResumeStrategyBlockFactory.Create(),
            "profit-fox-dca" => ProfitFoxDcaStrategyBlockFactory.Create(),
            "profit-fox-ladder" => ProfitFoxLadderStrategyBlockFactory.Create(),
            "capitulation-dip" => CapitulationDipStrategyBlockFactory.Create(),
            "volume-momentum" => VolumeMomentumStrategyBlockFactory.Create(),
            _ => throw new ArgumentOutOfRangeException(nameof(template), template, message: null)
        };
    }

    private static InstrumentRules Rules(string dataset)
    {
        return dataset.StartsWith("kasusdt", StringComparison.Ordinal)
            ? new InstrumentRules(quantityStep: 0.1m, priceTick: 0.00001m, minQuantity: 1m, minNotional: 5m, basePrecision: 1, quotePrecision: 8)
            : new InstrumentRules(quantityStep: 0.01m, priceTick: 0.0001m, minQuantity: 0.1m, minNotional: 5m, basePrecision: 2, quotePrecision: 8);
    }

    private static string FileName(string dataset)
    {
        return dataset switch
        {
            "kasusdt-4h" => "kasusdt-4h-20251002-20261012.csv",
            "wifusdt-4h" => "wifusdt-4h-20251002-20261012.csv",
            "kasusdt-5m" => "kasusdt-5m-20260901-20260908.csv",
            _ => throw new ArgumentOutOfRangeException(nameof(dataset), dataset, message: null)
        };
    }

    private static CompiledTradingGraph Compile(StrategyDefinition definition)
    {
        StrategyLoweringResult lowered = new StrategyDefinitionLowerer(Catalog).Lower(definition);
        TradingGraphCompilationResult compilation = new TradingGraphCompiler().Compile(lowered.Graph, Catalog);

        return compilation.Graph ?? throw new InvalidOperationException("Template did not compile.");
    }

    private static Candle[] ReadCandles(string path)
    {
        return File.ReadLines(path)
            .Where(line => !line.StartsWith('#'))
            .Skip(1)
            .Select(line => line.Split(','))
            .Select(cells => new Candle(
                DateTimeOffset.Parse(cells[0], CultureInfo.InvariantCulture),
                decimal.Parse(cells[1], CultureInfo.InvariantCulture),
                decimal.Parse(cells[2], CultureInfo.InvariantCulture),
                decimal.Parse(cells[3], CultureInfo.InvariantCulture),
                decimal.Parse(cells[4], CultureInfo.InvariantCulture),
                decimal.Parse(cells[5], CultureInfo.InvariantCulture)))
            .ToArray();
    }

    private static string Describe(BacktestResult result)
    {
        var text = new StringBuilder();
        BacktestTerminalSnapshot terminal = result.Terminal;
        AccountSnapshot account = terminal.Account;
        text.Append("outcome=")
            .Append(terminal.Outcome)
            .Append(" processed=")
            .Append(terminal.ProcessedCandles)
            .Append(" equity=")
            .Append(terminal.Equity)
            .Append(" unrealized=")
            .Append(terminal.UnrealizedPnl)
            .Append(" total=")
            .Append(terminal.TotalPnl)
            .Append('\n');
        text.Append("account quoteTotal=")
            .Append(account.QuoteTotal)
            .Append(" quoteReserved=")
            .Append(account.QuoteReserved)
            .Append(" baseTotal=")
            .Append(account.BaseTotal)
            .Append(" baseReserved=")
            .Append(account.BaseReserved)
            .Append(" costBasis=")
            .Append(account.CostBasis)
            .Append(" realized=")
            .Append(account.RealizedPnl)
            .Append(" feesBase=")
            .Append(account.FeesBase)
            .Append(" feesQuote=")
            .Append(account.FeesQuote)
            .Append(" lastBuy=")
            .Append(account.LastBuyPrice)
            .Append('\n');
        text.Append("terminalDiagnostic=").Append(terminal.Diagnostic).Append('\n');
        text.Append("counts ledger=").Append(result.LedgerCount).Append(" fills=").Append(result.FillCount).Append(" orders=").Append(result.OrderCount).Append('\n');

        foreach (BacktestLedgerEntry entry in result.Ledger)
        {
            text.Append(entry.Sequence)
                .Append('|')
                .Append(entry.Kind)
                .Append('|')
                .Append(entry.Context.CandleIndex)
                .Append('|')
                .Append(entry.Context.Phase)
                .Append('|')
                .Append(entry.Context.MarketPrice)
                .Append('|')
                .Append(entry.Order)
                .Append('|')
                .Append(entry.Fill)
                .Append('|')
                .Append(entry.Rejection)
                .Append('\n');
        }

        foreach (BacktestDiagnostic diagnostic in result.Diagnostics)
        {
            text.Append("diagnostic|").Append(diagnostic).Append('\n');
        }

        return text.ToString();
    }
}