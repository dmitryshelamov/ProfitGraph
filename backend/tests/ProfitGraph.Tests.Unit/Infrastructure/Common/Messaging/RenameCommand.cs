using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Tests.Unit.Infrastructure.Common.Messaging;

internal sealed record RenameCommand(string Name) : ICommand<string>;