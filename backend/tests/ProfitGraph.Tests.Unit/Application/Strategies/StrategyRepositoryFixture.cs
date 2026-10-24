using NSubstitute;

using ProfitGraph.Domain.Strategies;

namespace ProfitGraph.Tests.Unit.Application.Strategies;

internal sealed class StrategyRepositoryFixture
{
    private readonly Dictionary<Guid, Strategy> _strategies = new Dictionary<Guid, Strategy>();
    private readonly Dictionary<(Guid, int), StrategyVersion> _versions = new Dictionary<(Guid, int), StrategyVersion>();

    public IStrategyRepository Repository { get; } = Substitute.For<IStrategyRepository>();
    public bool RejectSave { get; set; }

    public StrategyRepositoryFixture()
    {
        Repository.Add(Arg.Any<Strategy>(), Arg.Any<StrategyVersion>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                call.Arg<CancellationToken>().ThrowIfCancellationRequested();
                var strategy = call.Arg<Strategy>()!;
                var version = call.Arg<StrategyVersion>()!;
                _strategies.Add(strategy.Id, Clone(strategy));
                _versions.Add((strategy.Id, version.Number), version);

                return Task.CompletedTask;
            });
        Repository.Get(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                _strategies.TryGetValue(call.Arg<Guid>(), out Strategy? value) ? Clone(value) : null);
        Repository.GetVersion(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                _versions.GetValueOrDefault((call.Arg<Guid>(), call.Arg<int>())));
        Repository.GetVersions(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                _versions.Where(pair => pair.Key.Item1 == call.Arg<Guid>())
                    .Select(pair => pair.Value)
                    .OrderByDescending(version => version.Number)
                    .ToArray());
        Repository.Save(Arg.Any<Strategy>(), Arg.Any<StrategyVersion>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var strategy = call.Arg<Strategy>()!;
                var version = call.Arg<StrategyVersion>()!;

                if (RejectSave || _strategies[strategy.Id].CurrentVersion != call.Arg<int>())
                {
                    return false;
                }

                _strategies[strategy.Id] = Clone(strategy);
                _versions.Add((strategy.Id, version.Number), version);

                return true;
            });
    }

    private static Strategy Clone(Strategy strategy)
    {
        return Strategy.Restore(strategy.Id, strategy.OwnerId, strategy.Name, strategy.CurrentVersion, strategy.CreatedAt, strategy.UpdatedAt);
    }
}