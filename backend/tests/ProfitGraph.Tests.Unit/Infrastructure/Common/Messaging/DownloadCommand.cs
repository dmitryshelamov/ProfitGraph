using ProfitGraph.Application.Common.Messaging;

namespace ProfitGraph.Tests.Unit.Infrastructure.Common.Messaging;

internal sealed record DownloadCommand : ICommand<string>, INonTransactionalCommand;