using NSubstitute;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.CreateStrategy;
using ProfitGraph.Application.Strategies.GetStrategy;
using ProfitGraph.Application.Strategies.GetStrategyVersion;
using ProfitGraph.Application.Strategies.SaveStrategy;
using ProfitGraph.Application.Strategies.ValidateStrategy;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Tests.Unit.Common;

namespace ProfitGraph.Tests.Unit.Application.Strategies;

public sealed class StrategyCommandTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly FixedClock Clock = new FixedClock(DateTimeOffset.UnixEpoch.AddDays(1));

    [Test]
    public async Task CreateUsesInjectedClockAndSaveKeepsOriginalSnapshot()
    {
        var fixture = new StrategyRepositoryFixture();
        IStrategyRepository repository = fixture.Repository;
        StrategyInternal created = await Create(repository);
        var handler = new SaveStrategyCommandHandler(repository, Clock);
        StrategyInternal saved = await handler.Handle(
            new SaveStrategyCommand(
                Owner,
                created.Id,
                ExpectedVersion: 1,
                "Changed",
                created.Definition,
                created.Layout),
            CancellationToken.None);
        StrategyVersionInternal original = await new GetStrategyVersionQueryHandler(repository).Handle(
            new GetStrategyVersionQuery(Owner, created.Id, Version: 1),
            CancellationToken.None);

        created.ShouldSatisfyAllConditions(
            () => created.CreatedAt.ShouldBe(Clock.GetUtcNow()),
            () => saved.CurrentVersion.ShouldBe(2),
            () => original.Number.ShouldBe(1),
            () => original.Definition.Blocks[0].Id.ShouldBe("source"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SaveRejectsStaleVersionBeforeAndDuringPersistence(bool conflictDuringSave)
    {
        var fixture = new StrategyRepositoryFixture();
        IStrategyRepository repository = fixture.Repository;
        StrategyInternal created = await Create(repository);
        fixture.RejectSave = conflictDuringSave;
        var handler = new SaveStrategyCommandHandler(repository, Clock);

        await Should.ThrowAsync<StateConflictException>(() => handler.Handle(
            new SaveStrategyCommand(
                Owner,
                created.Id,
                conflictDuringSave ? 1 : 2,
                created.Name,
                created.Definition,
                created.Layout),
            CancellationToken.None));
        (await repository.Get(new StrategyId(created.Id), CancellationToken.None))!.CurrentVersion.Value.ShouldBe(1);
    }

    [Test]
    public async Task UnknownStrategyIsKnownApplicationError()
    {
        var fixture = new StrategyRepositoryFixture();
        IStrategyRepository repository = fixture.Repository;
        await Should.ThrowAsync<ResourceNotFoundException>(() =>
            new GetStrategyQueryHandler(repository).Handle(new GetStrategyQuery(Owner, Guid.NewGuid()), CancellationToken.None));
    }

    [Test]
    public async Task ForeignStrategyIsReportedAsMissing()
    {
        var fixture = new StrategyRepositoryFixture();
        IStrategyRepository repository = fixture.Repository;
        StrategyInternal created = await Create(repository);
        var stranger = Guid.NewGuid();

        var read = await Should.ThrowAsync<ResourceNotFoundException>(() =>
            new GetStrategyQueryHandler(repository).Handle(new GetStrategyQuery(stranger, created.Id), CancellationToken.None));
        var save = await Should.ThrowAsync<ResourceNotFoundException>(() => new SaveStrategyCommandHandler(repository, Clock).Handle(
            new SaveStrategyCommand(
                stranger,
                created.Id,
                ExpectedVersion: 1,
                "Stolen",
                created.Definition,
                created.Layout),
            CancellationToken.None));

        read.Code.ShouldBe("strategy_not_found");
        save.Code.ShouldBe("strategy_not_found");
        (await repository.Get(new StrategyId(created.Id), CancellationToken.None))!.Name.Value.ShouldBe("Initial");
    }

    [Test]
    public async Task ValidationReportsSavedVersionWithoutWriting()
    {
        var fixture = new StrategyRepositoryFixture();
        IStrategyRepository repository = fixture.Repository;
        StrategyInternal created = await Create(repository);
        var handler = new ValidateStrategyQueryHandler(repository, new TestNodeTypeRegistry([]));
        StrategyValidationInternal validation = await handler.Handle(new ValidateStrategyQuery(Owner, created.Id), CancellationToken.None);

        validation.ShouldSatisfyAllConditions(
            () => validation.IsValid.ShouldBeFalse(),
            () => validation.Version.ShouldBe(1),
            () => validation.Diagnostics.Single().Code.ShouldBe("UnknownNodeType"));
        (await repository.GetVersions(new StrategyId(created.Id), CancellationToken.None)).Count.ShouldBe(1);
    }

    [Test]
    public async Task DraftValidationDoesNotReadOrAttributeSavedVersion()
    {
        var fixture = new StrategyRepositoryFixture();
        IStrategyRepository repository = fixture.Repository;
        StrategyInternal created = await Create(repository);
        repository.ClearReceivedCalls();
        var handler = new ValidateStrategyQueryHandler(repository, new TestNodeTypeRegistry([]));
        StrategyValidationInternal validation = await handler.Handle(
            new ValidateStrategyQuery(Owner, created.Id, Definition: created.Definition),
            CancellationToken.None);

        validation.Version.ShouldBeNull();
        await repository.DidNotReceive().GetVersion(Arg.Any<StrategyId>(), Arg.Any<StrategyVersionNumber>(), Arg.Any<CancellationToken>());
        await repository.DidNotReceive()
            .Add(
                Arg.Any<Strategy>(),
                Arg.Any<StrategyVersion>(),
                Arg.Any<CancellationToken>());
        (await repository.GetVersions(new StrategyId(created.Id), CancellationToken.None)).Count.ShouldBe(1);
    }

    [Test]
    public void ValidationRejectsAmbiguousDraftAndSavedVersion()
    {
        var validator = new ValidateStrategyQueryValidator();
        var graph = new StrategyDefinitionInternal([], [], [], []);
        validator.Validate(new ValidateStrategyQuery(Owner, Guid.NewGuid(), Version: 1, graph))
            .Errors
            .ShouldContain(error => error.PropertyName == nameof(ValidateStrategyQuery.Version));
        validator.Validate(new ValidateStrategyQuery(Owner, Guid.NewGuid(), Definition: graph)).IsValid.ShouldBeTrue();
        validator.Validate(new ValidateStrategyQuery(Owner, Guid.NewGuid(), Version: 1)).IsValid.ShouldBeTrue();
    }

    [Test]
    public void CreateValidationAllowsEmptyDraftButRejectsMalformedLiteral()
    {
        var validator = new CreateStrategyCommandValidator();
        var graph = new StrategyDefinitionInternal([], [], [], []);
        var layout = new GraphLayoutInternal(new Dictionary<string, NodePositionInternal>());
        validator.Validate(new CreateStrategyCommand(Owner, "Draft", graph, layout)).IsValid.ShouldBeTrue();
        var invalidGraph = new StrategyDefinitionInternal([], [], [new StrategyBlockInternal("", "event")], []);
        validator.Validate(new CreateStrategyCommand(Owner, "Draft", invalidGraph, layout)).IsValid.ShouldBeFalse();
    }

    [Test]
    public async Task CancelledCreateDoesNotPersistAnything()
    {
        var fixture = new StrategyRepositoryFixture();
        IStrategyRepository repository = fixture.Repository;
        await Should.ThrowAsync<OperationCanceledException>(() => new CreateStrategyCommandHandler(repository, Clock)
            .Handle(
                new CreateStrategyCommand(
                    Owner,
                    "Draft",
                    new StrategyDefinitionInternal([], [], [], []),
                    new GraphLayoutInternal(new Dictionary<string, NodePositionInternal>())),
                new CancellationToken(canceled: true)));
    }

    private static Task<StrategyInternal> Create(IStrategyRepository repository)
    {
        return new CreateStrategyCommandHandler(repository, Clock).Handle(
            new CreateStrategyCommand(
                Owner,
                "Initial",
                new StrategyDefinitionInternal([], [], [new StrategyBlockInternal("source", "event", Source: "OnStart")], []),
                new GraphLayoutInternal(new Dictionary<string, NodePositionInternal>())),
            CancellationToken.None);
    }
}