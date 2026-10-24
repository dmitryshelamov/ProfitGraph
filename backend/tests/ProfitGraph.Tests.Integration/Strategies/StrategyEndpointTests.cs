using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Strategies.Contracts;
using ProfitGraph.Application.Strategies.Converters;
using ProfitGraph.Application.Strategies.CreateStrategy;
using ProfitGraph.Application.Strategies.SaveStrategy;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Strategies;
using ProfitGraph.Infrastructure.Common.Postgres;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Strategies;

[NonParallelizable]
public sealed class StrategyEndpointTests
{
    [Test]
    public async Task StrategiesAreVisibleOnlyToTheirOwner()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient owner = await application.CreateSignedInClient();
        using HttpClient other = await application.CreateSignedInClient();
        JsonNode created = await Create(owner, StrategyRequestGenerator.Create());
        var id = created["id"]!.GetValue<string>();
        created["expectedVersion"] = 1;

        var ownList = (await owner.GetFromJsonAsync<JsonArray>("/api/strategies"))!;
        var otherList = (await other.GetFromJsonAsync<JsonArray>("/api/strategies"))!;
        using HttpResponseMessage get = await other.GetAsync($"/api/strategies/{id}");
        using HttpResponseMessage versions = await other.GetAsync($"/api/strategies/{id}/versions");
        using HttpResponseMessage version = await other.GetAsync($"/api/strategies/{id}/versions/1");
        using HttpResponseMessage save = await other.PutAsJsonAsync($"/api/strategies/{id}", created);
        using HttpResponseMessage validate = await other.PostAsync($"/api/strategies/{id}/validate", content: null);

        ownList.Select(item => item!["id"]!.GetValue<string>()).ShouldBe([id]);
        otherList.ShouldBeEmpty();

        foreach (HttpResponseMessage response in new[] { get, versions, version, save, validate })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await response.Content.ReadFromJsonAsync<JsonNode>())!["code"]!.GetValue<string>().ShouldBe("strategy_not_found");
        }

        (await owner.GetFromJsonAsync<JsonNode>($"/api/strategies/{id}"))!["currentVersion"]!.GetValue<int>().ShouldBe(1);
    }

    [Test]
    public async Task AnonymousRequestsAreRejected()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = application.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/strategies");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task OldStrategyFormatHasDistinctConflictCodeAndDoesNotModifyStoredData()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        JsonNode created = await Create(client, StrategyRequestGenerator.Create());
        var id = created["id"]!.GetValue<string>();
        const string old = "{\"formatVersion\":0,\"graph\":{\"nodes\":[],\"connections\":[]},\"layout\":{\"positions\":{}}}";
        using IServiceScope scope = application.Services.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<NpgsqlConnection>();
        await connection.ExecuteAsync("UPDATE strategy_versions SET snapshot_json = @old::jsonb WHERE strategy_id = @id", new { old, id = Guid.Parse(id) });
        var before = await connection.QuerySingleAsync<string>("SELECT snapshot_json::text FROM strategy_versions WHERE strategy_id = @id", new { id = Guid.Parse(id) });
        using HttpResponseMessage response = await client.GetAsync($"/api/strategies/{id}");
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        problem["code"]!.GetValue<string>().ShouldBe("unsupported_strategy_format");
        problem["detail"]!.GetValue<string>().ShouldContain("recreated");
        (await connection.QuerySingleAsync<string>("SELECT snapshot_json::text FROM strategy_versions WHERE strategy_id = @id", new { id = Guid.Parse(id) })).ShouldBe(
            before);
    }

    [Test]
    public async Task ApplicationCreateCommand_UsesTransactionalPipeline()
    {
        await using var application = new ProfitGraphApiFactory();
        UserInternal user = await application.CreateAccount(UserRole.User);
        using IServiceScope scope = application.Services.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var definition = new StrategyDefinitionInternal([], [], [], []);
        StrategyInternal created = await sender.Send(
            new CreateStrategyCommand(
                user.Id,
                "Direct",
                definition,
                new GraphLayoutInternal(new Dictionary<string, NodePositionInternal>())));

        created.CurrentVersion.ShouldBe(1);
    }

    [Test]
    public async Task SaveReloadValidateAndHistory_PreserveGraphAndLayout()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        JsonNode created = await Create(client, StrategyRequestGenerator.Create());
        var id = created["id"]!.GetValue<string>();
        using HttpResponseMessage load = await client.GetAsync($"/api/strategies/{id}");
        var loaded = (await load.Content.ReadFromJsonAsync<JsonNode>())!;

        load.ShouldSatisfyAllConditions(
            () => load.StatusCode.ShouldBe(HttpStatusCode.OK),
            () => JsonNode.DeepEquals(created["definition"], loaded["definition"]).ShouldBeTrue(),
            () => JsonNode.DeepEquals(created["layout"], loaded["layout"]).ShouldBeTrue());

        loaded["expectedVersion"] = 1;
        loaded["name"] = "Updated";
        loaded["definition"]!["parameters"]![0]!["defaultValue"] = "3";
        using HttpResponseMessage save = await client.PutAsJsonAsync($"/api/strategies/{id}", loaded);
        var saved = (await save.Content.ReadFromJsonAsync<JsonNode>())!;
        using HttpResponseMessage validation = await client.PostAsync($"/api/strategies/{id}/validate", content: null);
        var diagnostics = (await validation.Content.ReadFromJsonAsync<JsonNode>())!;
        using HttpResponseMessage history = await client.GetAsync($"/api/strategies/{id}/versions");
        var versions = (await history.Content.ReadFromJsonAsync<JsonArray>())!;
        using HttpResponseMessage old = await client.GetAsync($"/api/strategies/{id}/versions/1");
        var oldVersion = (await old.Content.ReadFromJsonAsync<JsonNode>())!;

        save.ShouldSatisfyAllConditions(
            () => save.StatusCode.ShouldBe(HttpStatusCode.OK),
            () => saved["currentVersion"]!.GetValue<int>().ShouldBe(2),
            () => diagnostics["isValid"]!.GetValue<bool>().ShouldBeTrue(),
            () => diagnostics["version"]!.GetValue<int>().ShouldBe(2),
            () => versions.Select(version => version!["number"]!.GetValue<int>()).ShouldBe([2, 1]),
            () => oldVersion["definition"]!["parameters"]![0]!["defaultValue"]!.GetValue<string>().ShouldBe("2"));

        using HttpResponseMessage stale = await client.PutAsJsonAsync($"/api/strategies/{id}", loaded);
        await AssertProblem(stale, HttpStatusCode.Conflict, "strategy_changed");
    }

    [Test]
    public async Task LaneDisplayNameSurvivesCreateAndSave()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        JsonNode request = JsonSerializer.SerializeToNode(StrategyRequestGenerator.Create())!;
        request["definition"]!["blocks"]![0]!["displayName"] = "Первый вход";
        JsonNode created = await Create(client, request);
        var id = created["id"]!.GetValue<string>();

        created["definition"]!["blocks"]![0]!["displayName"]!.GetValue<string>().ShouldBe("Первый вход");

        using HttpResponseMessage load = await client.GetAsync($"/api/strategies/{id}");
        var saved = (await load.Content.ReadFromJsonAsync<JsonNode>())!;
        saved["definition"]!["blocks"]![0]!["displayName"]!.GetValue<string>().ShouldBe("Первый вход");

        saved["expectedVersion"] = 1;
        saved["definition"]!["blocks"]![0]!["displayName"] = "Второй вход";
        using HttpResponseMessage update = await client.PutAsJsonAsync($"/api/strategies/{id}", saved);
        var updated = (await update.Content.ReadFromJsonAsync<JsonNode>())!;
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        updated["definition"]!["blocks"]![0]!["displayName"]!.GetValue<string>().ShouldBe("Второй вход");
    }

    [Test]
    public async Task InvalidDraftCanBeSavedAndReturnsGraphDiagnostics()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        JsonNode created = await Create(client, StrategyRequestGenerator.Create(period: 0));
        using HttpResponseMessage response = await client.PostAsync($"/api/strategies/{created["id"]}/validate", content: null);
        var validation = (await response.Content.ReadFromJsonAsync<JsonNode>())!;

        response.ShouldSatisfyAllConditions(
            () => response.StatusCode.ShouldBe(HttpStatusCode.OK),
            () => validation["isValid"]!.GetValue<bool>().ShouldBeFalse(),
            () => validation["diagnostics"]!.AsArray()
                .Any(diagnostic =>
                    diagnostic!["code"]!.GetValue<string>() == "InvalidParameter" &&
                    diagnostic["parameterName"]?.GetValue<string>() == "period")
                .ShouldBeTrue());
    }

    [Test]
    public async Task ParameterReferencesAndNamedValueTreesRoundTripThroughJsonb()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        JsonNode created = await Create(client, StrategyRequestGenerator.Create());
        using HttpResponseMessage load = await client.GetAsync($"/api/strategies/{created["id"]}");
        load.StatusCode.ShouldBe(HttpStatusCode.OK);
        var loaded = (await load.Content.ReadFromJsonAsync<JsonNode>())!;
        JsonNode.DeepEquals(created["definition"], loaded["definition"]).ShouldBeTrue();
        loaded["definition"]!["values"]![0]!["expression"]!["arguments"]!["period"]!["name"]!.GetValue<string>().ShouldBe("period");
    }

    [TestCase("true")]
    [TestCase("null")]
    [TestCase("1")]
    public async Task MalformedDecimalJsonUsesProblemDetails(string parameterJson)
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        JsonNode payload = JsonSerializer.SerializeToNode(StrategyRequestGenerator.Create())!;
        payload["definition"]!["parameters"]![0]!["defaultValue"] = JsonNode.Parse(parameterJson);
        using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync("/api/strategies", content);
        await AssertProblem(response, HttpStatusCode.BadRequest, "validation_failed");
    }

    [Test]
    public async Task MissingStrategyUsesProblemDetails()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        using HttpResponseMessage response = await client.GetAsync($"/api/strategies/{Guid.NewGuid()}");
        await AssertProblem(response, HttpStatusCode.NotFound, "strategy_not_found");
    }

    [Test]
    public async Task TransactionRollbackDoesNotLeaveNewVersion()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        JsonNode created = await Create(client, StrategyRequestGenerator.Create());
        Guid id = Guid.Parse(created["id"]!.GetValue<string>());
        using IServiceScope scope = application.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IStrategyRepository>();
        var executor = scope.ServiceProvider.GetRequiredService<PostgresTransactionExecutor>();
        var strategyId = new StrategyId(id);
        Guid owner = (await repository.Get(strategyId, CancellationToken.None))!.OwnerId.Value;
        var handler = new SaveStrategyCommandHandler(repository, scope.ServiceProvider.GetRequiredService<TimeProvider>());
        StrategyVersion version = (await repository.GetVersion(strategyId, StrategyVersionNumber.First, CancellationToken.None))!;

        await Should.ThrowAsync<InvalidOperationException>(async () => await executor.ExecuteAsync<StrategyInternal>(
            async () =>
            {
                await handler.Handle(
                    new SaveStrategyCommand(
                        owner,
                        id,
                        ExpectedVersion: 1,
                        "Rolled back",
                        version.Definition.ToInternal(),
                        version.Layout.ToInternal()),
                    CancellationToken.None);

                throw new InvalidOperationException("Rollback fixture.");
            },
            CancellationToken.None));

        Strategy after = (await repository.Get(strategyId, CancellationToken.None))!;
        after.ShouldSatisfyAllConditions(
            () => after.CurrentVersion.ShouldBe(StrategyVersionNumber.First),
            () => after.Name.Value.ShouldBe("SMA strategy"));
        (await repository.GetVersions(strategyId, CancellationToken.None)).Count.ShouldBe(1);
    }

    [Test]
    public async Task ConcurrentSavesAllowOnlyOneNewVersion()
    {
        await using var application = new ProfitGraphApiFactory();
        using HttpClient client = await application.CreateSignedInClient();
        JsonNode created = await Create(client, StrategyRequestGenerator.Create());
        var id = created["id"]!.GetValue<string>();
        created["expectedVersion"] = 1;
        HttpResponseMessage[] responses = await Task.WhenAll(
            client.PutAsJsonAsync($"/api/strategies/{id}", created),
            client.PutAsJsonAsync($"/api/strategies/{id}", created));
        using HttpResponseMessage first = responses[0];
        using HttpResponseMessage second = responses[1];
        new[] { first.StatusCode, second.StatusCode }.ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict], ignoreOrder: true);
        using HttpResponseMessage history = await client.GetAsync($"/api/strategies/{id}/versions");
        (await history.Content.ReadFromJsonAsync<JsonArray>())!.Count.ShouldBe(2);
    }

    private static async Task<JsonNode> Create(HttpClient client, object request)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/strategies", request);
        string body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, body);
        response.Headers.Location.ShouldNotBeNull();

        return JsonNode.Parse(body)!;
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var problem = (await response.Content.ReadFromJsonAsync<JsonNode>())!;
        response.ShouldSatisfyAllConditions(
            () => response.StatusCode.ShouldBe(status),
            () => response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json"),
            () => problem["code"]!.GetValue<string>().ShouldBe(code));
    }
}