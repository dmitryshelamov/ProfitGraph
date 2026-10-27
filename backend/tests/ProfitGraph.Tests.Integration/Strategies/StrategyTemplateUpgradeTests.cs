using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Converters;
using ProfitGraph.Application.Strategies.CreateStrategy;
using ProfitGraph.Application.Strategies.UpgradeStrategyTemplates;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Engine.Blocks;
using ProfitGraph.Engine.Templates;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Strategies;

/// <summary>
///     The upgrade of saved strategies to the later revision of their template (ADR 0038): a saved Ladder with resume that lacks the
///     reaction to a filled manual order gets it as a new version, once; anything else is left as it is.
/// </summary>
[NonParallelizable]
public sealed class StrategyTemplateUpgradeTests
{
    private static readonly string[] ManualBlocks = ["manualFilled", "manualCancelAverage"];

    private ProfitGraphApiFactory _api = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _api = new ProfitGraphApiFactory();
        _ = _api.Services;
    }

    [OneTimeTearDown]
    public async Task Stop()
    {
        await _api.DisposeAsync();
    }

    [Test]
    public async Task ASavedLadderWithResumeGetsTheManualBlocksAsANewVersion_OnceAndNothingElseChanges()
    {
        Guid owner = await NewOwner();
        StrategyInternal saved = await Create(owner, "Saved before ADR 0037", Without(LadderSmaStrategyBlockFactory.Create()));
        StrategyInternal plain = await Create(owner, "Plain ladder", LadderStrategyBlockFactory.Create());
        StrategyInternal current = await Create(owner, "Made from today's template", LadderSmaStrategyBlockFactory.Create());

        IReadOnlyList<UpgradedStrategyInternal> first = await Ask(new UpgradeStrategyTemplatesCommand());
        IReadOnlyList<UpgradedStrategyInternal> second = await Ask(new UpgradeStrategyTemplatesCommand());

        first.ShouldContain(upgraded => upgraded.StrategyId == saved.Id && upgraded.Version == 2);
        first.ShouldNotContain(upgraded => upgraded.StrategyId == plain.Id || upgraded.StrategyId == current.Id);
        second.ShouldNotContain(upgraded => upgraded.StrategyId == saved.Id);
        IReadOnlyList<StrategyVersion> versions = await Versions(saved.Id);
        versions.Select(version => version.Number.Value).Order().ShouldBe([1, 2]);
        StrategyVersion upgradedVersion = versions.Single(version => version.Number.Value == 2);
        upgradedVersion.Definition.Blocks.Select(block => block.Id)
            .ShouldBe(LadderSmaStrategyBlockFactory.Create().Blocks.Select(block => block.Id), ignoreOrder: true);
        upgradedVersion.Definition.Parameters.ShouldBe(versions.Single(version => version.Number.Value == 1).Definition.Parameters);
        (await Versions(plain.Id)).Count.ShouldBe(1);
        (await Versions(current.Id)).Count.ShouldBe(1);
    }

    [Test]
    public async Task AStrategyStoredInAnOldFormatDoesNotStopTheOthersFromBeingUpgraded()
    {
        Guid owner = await NewOwner();
        StrategyInternal old = await Create(owner, "Stored in an old format", Without(LadderSmaStrategyBlockFactory.Create()));
        StrategyInternal saved = await Create(owner, "Saved before ADR 0037", Without(LadderSmaStrategyBlockFactory.Create()));
        await using (var connection = new NpgsqlConnection(TestDatabase.ConnectionString))
        {
            await connection.ExecuteAsync(
                "UPDATE strategy_versions SET snapshot_json = '{\"formatVersion\": 0}'::jsonb WHERE strategy_id = @id",
                new { id = old.Id });
        }

        IReadOnlyList<UpgradedStrategyInternal> upgraded = await Ask(new UpgradeStrategyTemplatesCommand());

        upgraded.ShouldContain(item => item.StrategyId == saved.Id);
        upgraded.ShouldNotContain(item => item.StrategyId == old.Id);
    }

    private static StrategyDefinition Without(StrategyDefinition template)
    {
        return new StrategyDefinition(
            template.Parameters,
            template.Values,
            template.Blocks.Where(block => !ManualBlocks.Contains(block.Id)),
            template.Wires.Where(wire => !ManualBlocks.Contains(wire.From.Block) && !ManualBlocks.Contains(wire.To)),
            template.TemplateKind);
    }

    private async Task<Guid> NewOwner()
    {
        var owner = UserId.New();
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);
        await connection.ExecuteAsync(
            "INSERT INTO users (id, login, password_hash, role, status, created_at, updated_at) " +
            "VALUES (@owner, 'upgrade-' || @owner, 'not-a-real-hash', 'User', 'Active', now(), now())",
            new { owner = owner.Value });

        return owner.Value;
    }

    private Task<StrategyInternal> Create(Guid owner, string name, StrategyDefinition definition)
    {
        return Ask(new CreateStrategyCommand(owner, name, definition.ToInternal(), new GraphLayoutInternal(new Dictionary<string, NodePositionInternal>())));
    }

    private async Task<IReadOnlyList<StrategyVersion>> Versions(Guid strategy)
    {
        using IServiceScope scope = _api.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IStrategyRepository>().GetVersions(new StrategyId(strategy), CancellationToken.None);
    }

    private async Task<T> Ask<T>(IRequest<T> request)
    {
        using IServiceScope scope = _api.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}
