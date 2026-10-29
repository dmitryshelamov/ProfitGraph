using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.MarketData;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Accounting;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Compilation;
using ProfitGraph.Engine.Evaluation;
using ProfitGraph.Engine.Grids;

namespace ProfitGraph.Application.Bots;

/// <summary>
///     What a bot's grid needs from its venue and fees (ADR 0058, sections 8 and 9): the venue states how many resting orders a
///     bot may keep, the grid's buys plus its levels' sales fit in them, and its profit step pays the bot's fees. Checked when a
///     bot is created, started or given new values, and when its runner takes a new strategy version.
/// </summary>
public sealed class BotGridRules(VenuePorts<IVenueRestingOrderCapacity> capacities)
{
    public static bool HasGrid(StrategyDefinition definition)
    {
        return definition.Blocks.Any(block => block.Kind == "grid");
    }

    public void Ensure(CandleSeries series, BotFeeRates rates, CompiledTradingGraph plan)
    {
        IReadOnlyDictionary<string, GridSettings?> grids = GridStatics.Read(plan, EngineExecutionInputs.Empty);

        if (grids.Count == 0)
        {
            return;
        }

        IVenueRestingOrderCapacity capacity = capacities.Find(series.Venue) ?? throw Refused(
            BotErrorCodes.GridNotSupportedByVenue,
            "This venue does not run grid strategies yet.");
        int limit = capacity.MaxRestingOrders(series.Symbol);
        int needed = grids.Values.Sum(settings => settings is null ? 0 : settings.MaxOpenBuys + settings.Levels);

        if (needed > limit)
        {
            throw Refused(BotErrorCodes.GridExceedsVenueOrders, $"The grid may keep {needed} orders at once; the venue allows {limit} for one bot.");
        }

        ExecutionSettings settings = rates.SellerFees is { } sellerFees
            ? new ExecutionSettings(startingCapital: 1m, slippage: 0m, sellerFees)
            : new ExecutionSettings(startingCapital: 1m, slippage: 0m, rates.LimitRate, rates.MarketRate);

        if (GridFeeCheck.Check(plan, EngineExecutionInputs.Empty, settings).FirstOrDefault(finding => finding.IsError) is { } error)
        {
            throw Refused(BotErrorCodes.GridProfitBelowFees, error.Message);
        }
    }

    private static ApplicationValidationException Refused(string code, string message)
    {
        return new ApplicationValidationException([new InputError(code, "Strategy", message)]);
    }
}