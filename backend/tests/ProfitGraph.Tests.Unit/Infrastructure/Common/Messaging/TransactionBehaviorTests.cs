using NSubstitute;

using ProfitGraph.Application.Common.Messaging;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Infrastructure.Common.Messaging;

namespace ProfitGraph.Tests.Unit.Infrastructure.Common.Messaging;

public sealed class TransactionBehaviorTests
{
    [Test]
    public async Task CommandRunsInsideTransaction()
    {
        var executor = Substitute.For<ITransactionExecutor>();
        executor.ExecuteAsync(Arg.Any<Func<Task<string>>>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<Func<Task<string>>>()!());
        var behavior = new TransactionBehavior<RenameCommand, string>(executor);

        string result = await behavior.Handle(new RenameCommand(), _ => Task.FromResult("renamed"), CancellationToken.None);

        result.ShouldBe("renamed");
        await executor.Received(1).ExecuteAsync(Arg.Any<Func<Task<string>>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task QueryRunsWithoutTransaction()
    {
        var executor = Substitute.For<ITransactionExecutor>();
        var behavior = new TransactionBehavior<NameQuery, string>(executor);

        string result = await behavior.Handle(new NameQuery(), _ => Task.FromResult("name"), CancellationToken.None);

        result.ShouldBe("name");
        await executor.DidNotReceiveWithAnyArgs().ExecuteAsync<string>(default!, cancellationToken: default);
    }

    [Test]
    public async Task NonTransactionalCommandRunsWithoutTransaction()
    {
        var executor = Substitute.For<ITransactionExecutor>();
        var behavior = new TransactionBehavior<DownloadCommand, string>(executor);

        string result = await behavior.Handle(new DownloadCommand(), _ => Task.FromResult("downloaded"), CancellationToken.None);

        result.ShouldBe("downloaded");
        await executor.DidNotReceiveWithAnyArgs().ExecuteAsync<string>(default!, cancellationToken: default);
    }

    internal sealed record RenameCommand : ICommand<string>;

    internal sealed record NameQuery : IQuery<string>;

    internal sealed record DownloadCommand : ICommand<string>, INonTransactionalCommand;
}