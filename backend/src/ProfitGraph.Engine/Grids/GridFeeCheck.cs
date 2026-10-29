using System.Globalization;

using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;

namespace ProfitGraph.Engine.Grids;

/// <summary>
///     Checks a grid's static steps against the fees of one round trip, a limit buy and a limit sale (ADR 0058, section 8). The
///     round trip costs two limit fee rates, or with seller fees per unit the effective seller rate at the reference price.
///     Profit at or below the round trip is an error; a step or profit below it plus slippage is a warning. Without instrument
///     rules or a reference price, seller fees cannot be priced and the check finds nothing; replay checks again with both.
/// </summary>
public static class GridFeeCheck
{
    public static IReadOnlyList<GridFeeFinding> Check(
        CompiledTradingGraph graph,
        EngineExecutionInputs selections,
        ExecutionSettings settings,
        InstrumentRules? rules = null,
        decimal referencePrice = 0m)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.SellerFees is not null && (rules is null || referencePrice <= 0m))
        {
            return [];
        }

        IReadOnlyDictionary<string, GridSettings?> grids = GridStatics.Read(graph, selections);

        if (grids.Count == 0)
        {
            return [];
        }

        decimal roundTrip = RoundTrip(settings, rules, referencePrice);
        decimal margin = roundTrip + settings.Slippage;
        var findings = new List<GridFeeFinding>();

        foreach ((string gridNodeId, GridSettings? grid) in grids)
        {
            decimal? step = grid?.Step;
            decimal? profit = grid?.ProfitStep;

            if (profit is { } profitStep && profitStep <= roundTrip)
            {
                findings.Add(
                    new GridFeeFinding(
                        gridNodeId,
                        IsError: true,
                        GridFeeFinding.ProfitBelowFees,
                        $"The grid's profit step {Percent(profitStep)} does not cover the round trip's fees {Percent(roundTrip)}: every cycle loses."));

                continue;
            }

            if ((step is { } gridStep && gridStep < margin) || (profit is { } near && near < margin))
            {
                findings.Add(
                    new GridFeeFinding(
                        gridNodeId,
                        IsError: false,
                        GridFeeFinding.StepNearFees,
                        $"The grid's step or profit step is below the round trip's fees plus slippage {Percent(margin)}."));
            }
        }

        return findings.AsReadOnly();
    }

    private static decimal RoundTrip(ExecutionSettings settings, InstrumentRules? rules, decimal referencePrice)
    {
        if (settings.SellerFees is { } sellerFees)
        {
            return sellerFees.Fee(quantity: 1m, referencePrice, rules!.QuotePrecision) / referencePrice;
        }

        return 2m * settings.LimitFeeRate;
    }

    private static string Percent(decimal fraction)
    {
        return (fraction * 100m).ToString("0.####", CultureInfo.InvariantCulture) + "%";
    }
}