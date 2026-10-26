using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Strategies.Contracts;

namespace ProfitGraph.Application.Strategies.RenameStrategy;

/// <summary>
///     Renames the owner's strategy without saving a version, so bots of the strategy keep running their version.
/// </summary>
public sealed record RenameStrategyCommand(Guid UserId, Guid Id, string Name) : ICommand<StrategyInternal>;