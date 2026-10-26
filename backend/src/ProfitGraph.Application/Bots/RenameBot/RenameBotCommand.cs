using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Application.Bots.RenameBot;

/// <summary>
///     Names the owner's bot in any state, or clears the name when <see cref="Name" /> is null or blank. It changes neither
///     trading nor the desired state.
/// </summary>
public sealed record RenameBotCommand(Guid OwnerId, Guid Id, string? Name) : ICommand<BotInternal>;