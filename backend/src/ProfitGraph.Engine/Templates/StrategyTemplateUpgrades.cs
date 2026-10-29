using ProfitGraph.Engine.Blocks;

namespace ProfitGraph.Engine.Templates;

/// <summary>
///     Additive upgrades of saved strategies to a later revision of their template (ADR 0038, ADR 0040). An upgrade only adds blocks and wires
///     to the graph the strategy has, after checking that the graph has the shape the addition attaches to; it never removes or
///     changes anything, and it is idempotent. A graph of another shape is left as it is.
/// </summary>
public static class StrategyTemplateUpgrades
{
    /// <summary>
    ///     The blocks the Ladder's reaction to a filled manual order (ADR 0037) and its restoring at resume (ADR 0040) attach to: the
    ///     exit and averaging chain of the Ladder and the restart condition. A graph without one of them, or without one of the chain's
    ///     wires, has been changed away from the template.
    /// </summary>
    private static readonly string[] ChainBlocks = ["entryConditions", "cancelExit", "exitConditions", "exit", "averageConditions", "restartEvent"];

    private static readonly (string From, string Exit, string To)[] RequiredWires =
    [
        ("cancelExit", "completed", "exitConditions"),
        ("exitConditions", "yes", "exit"),
        ("exit", "submitted", "averageConditions"),
        ("restartEvent", "yes", "entryConditions")
    ];

    /// <summary>
    ///     The definition brought to what the Ladder template of today has: the restoring at resume when the graph has none, then the
    ///     reaction to a filled manual order. Null when there is nothing to add or the graph is not the Ladder.
    /// </summary>
    public static StrategyDefinition? Upgrade(StrategyDefinition definition)
    {
        StrategyDefinition? resumed = WithResumeRestore(definition);
        StrategyDefinition? rebuilt = WithManualFillRebuild(resumed ?? definition);

        return rebuilt ?? resumed;
    }

    /// <summary>
    ///     The definition with the Ladder's restoring at resume added (ADR 0040), or null when the graph has a resume event already or is
    ///     not the Ladder chain. It makes a bot of a Ladder saved before the resume blocks existed place its exit again after a start or
    ///     after an order the venue cancelled.
    /// </summary>
    public static StrategyDefinition? WithResumeRestore(StrategyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        HashSet<string> ids = [.. definition.Blocks.Select(block => block.Id)];
        IReadOnlyList<StrategyBlock> blocks = LadderResumeRestore.Blocks();

        if (blocks.Any(block => ids.Contains(block.Id)) || definition.Blocks.Any(IsResumeEvent) || ChainBlocks.Any(id => !ids.Contains(id)) ||
            !HasWires(definition))
        {
            return null;
        }

        return new StrategyDefinition(
            definition.Parameters,
            definition.Values,
            [.. definition.Blocks, .. blocks],
            [.. definition.Wires, .. LadderResumeRestore.Wires()],
            definition.TemplateKind,
            definition.Direction);
    }

    private static bool HasWires(StrategyDefinition definition)
    {
        return RequiredWires.All(
            wire => definition.Wires.Any(candidate => candidate.From.Block == wire.From && candidate.From.Exit == wire.Exit && candidate.To == wire.To));
    }

    /// <summary>
    ///     The definition with the Ladder's reaction to a filled manual order added, or null when the graph already reacts to the
    ///     manual tag or is not the Ladder with resume. The resume event may be the template's <c>resume</c> or one the owner added under
    ///     another id (ADR 0052).
    /// </summary>
    public static StrategyDefinition? WithManualFillRebuild(StrategyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        HashSet<string> ids = [.. definition.Blocks.Select(block => block.Id)];
        IReadOnlyList<StrategyBlock> blocks = LadderManualFillRebuild.Blocks();

        if (blocks.Any(block => ids.Contains(block.Id)) || definition.Blocks.Any(ReactsToManualOrders) || !definition.Blocks.Any(IsResumeEvent) ||
            ChainBlocks.Any(id => !ids.Contains(id)) || !HasWires(definition))
        {
            return null;
        }

        return new StrategyDefinition(
            definition.Parameters,
            definition.Values,
            [.. definition.Blocks, .. blocks],
            [.. definition.Wires, .. LadderManualFillRebuild.Wires()],
            definition.TemplateKind,
            definition.Direction);
    }

    private static bool IsResumeEvent(StrategyBlock block)
    {
        return block is { Kind: "event", Source: "OnResume" };
    }

    private static bool ReactsToManualOrders(StrategyBlock block)
    {
        return block is { Kind: "event", Source: "OnOrderFilled", Tag: { Kind: "constant", Text: "manual" } };
    }
}
