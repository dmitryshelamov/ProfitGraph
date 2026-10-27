using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Strategies.Contracts;

namespace ProfitGraph.Application.Strategies.UpgradeStrategyTemplates;

/// <summary>
///     Saves a new version of every strategy whose graph is a template's earlier revision that an upgrade can bring forward
///     (ADR 0038); returns the strategies it saved. Running bots follow the new version on their next root (ADR 0019).
/// </summary>
public sealed record UpgradeStrategyTemplatesCommand : ICommand<IReadOnlyList<UpgradedStrategyInternal>>;
