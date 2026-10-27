using ProfitGraph.Engine.Blocks;

namespace ProfitGraph.Engine.Templates;

/// <summary>
///     Additive upgrades of saved strategies to a later revision of their template (ADR 0038). An upgrade only adds blocks and wires
///     to the graph the strategy has, after checking that the graph has the shape the addition attaches to; it never removes or
///     changes anything, and it is idempotent. A graph of another shape is left as it is.
/// </summary>
public static class StrategyTemplateUpgrades
{
    /// <summary>
    ///     The blocks the Ladder's reaction to a filled manual order attaches to (ADR 0037): the exit and averaging chain of the Ladder
    ///     with resume, and the restart condition. A graph without one of them, or without one of the chain's wires, has been changed
    ///     away from the template.
    /// </summary>
    private static readonly string[] RequiredBlocks = ["resume", "cancelExit", "exitConditions", "exit", "averageConditions", "restartEvent"];

    private static readonly (string From, string Exit, string To)[] RequiredWires =
    [
        ("cancelExit", "completed", "exitConditions"),
        ("exitConditions", "yes", "exit"),
        ("exit", "submitted", "averageConditions"),
        ("restartEvent", "yes", "entryConditions")
    ];

    /// <summary>
    ///     The definition with the Ladder's reaction to a filled manual order added, or null when the graph already reacts to the
    ///     manual tag or is not the Ladder with resume.
    /// </summary>
    public static StrategyDefinition? WithManualFillRebuild(StrategyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        HashSet<string> ids = [.. definition.Blocks.Select(block => block.Id)];
        IReadOnlyList<StrategyBlock> blocks = LadderManualFillRebuild.Blocks();

        if (blocks.Any(block => ids.Contains(block.Id)) || definition.Blocks.Any(ReactsToManualOrders) ||
            RequiredBlocks.Any(id => !ids.Contains(id)) ||
            RequiredWires.Any(
                wire => !definition.Wires.Any(candidate => candidate.From.Block == wire.From && candidate.From.Exit == wire.Exit && candidate.To == wire.To)))
        {
            return null;
        }

        return new StrategyDefinition(
            definition.Parameters,
            definition.Values,
            [.. definition.Blocks, .. blocks],
            [.. definition.Wires, .. LadderManualFillRebuild.Wires()],
            definition.TemplateKind);
    }

    private static bool ReactsToManualOrders(StrategyBlock block)
    {
        return block is { Kind: "event", Source: "OnOrderFilled", Tag: { Kind: "constant", Text: "manual" } };
    }
}
