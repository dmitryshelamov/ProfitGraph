using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Api.Trading.Contracts;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     The bot kill switch, the global halt and alerts over HTTP (stage 5.9, ADR 0018), on the test venue with invented
///     credentials. It has its own API, so its sign-ins do not share the other bot tests' rate limit.
/// </summary>
[NonParallelizable]
public sealed class BotControlEndpointTests
{
    private static readonly JsonSerializerOptions WebJson = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private ProfitGraphApiFactory _factory = null!;

    [OneTimeSetUp]
    public void StartApi()
    {
        _factory = new ProfitGraphApiFactory(BotApi.Settings());
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task KillStopsTheBotUntilItIsStartedAgain()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        await owner.Client.PostAsync($"{BotApi.Path}/{id}/start", content: null);

        JsonElement killed = await BotApi.Json(await owner.Client.PostAsync($"{BotApi.Path}/{id}/kill", content: null));
        JsonElement restarted = await BotApi.Json(await owner.Client.PostAsync($"{BotApi.Path}/{id}/start", content: null));

        killed.GetProperty("desiredState").GetString().ShouldBe("Stopped");
        killed.GetProperty("statusReason").GetString().ShouldBe("Killed");
        restarted.GetProperty("desiredState").GetString().ShouldBe("Running");
        restarted.GetProperty("statusReason").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Test]
    public async Task OnlyTheOwnerHaltsAndTheHaltKillsRunningBots()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        await owner.Client.PostAsync($"{BotApi.Path}/{id}/start", content: null);
        HttpClient admin = await _factory.SignIn(await _factory.CreateAccount(UserRole.Owner));

        (await owner.Client.PostAsync("/api/trading-halt/halt", content: null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        try
        {
            DateTimeOffset before = DateTimeOffset.UtcNow;
            var halted = (await (await admin.PostAsync("/api/trading-halt/halt", content: null)).Content.ReadFromJsonAsync<TradingHaltResponse>())!;
            JsonElement seen = await BotApi.Json(await owner.Client.GetAsync("/api/trading-halt"));
            JsonElement bot = await BotApi.Json(await owner.Client.GetAsync($"{BotApi.Path}/{id}"));

            halted.IsHalted.ShouldBeTrue();
            halted.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));
            seen.GetProperty("isHalted").GetBoolean().ShouldBeTrue();
            bot.GetProperty("statusReason").GetString().ShouldBe("Killed");
            bot.GetProperty("desiredState").GetString().ShouldBe("Stopped");
        }
        finally
        {
            JsonElement resumed = await BotApi.Json(await admin.PostAsync("/api/trading-halt/resume", content: null));
            resumed.GetProperty("isHalted").GetBoolean().ShouldBeFalse();
        }
    }

    [Test]
    public async Task AlertsArePrivateToTheBotsOwnerAndAcknowledgedOnce()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        BotOwner other = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        Guid alert = await RaiseAlert(id, BotAlertKind.Degraded, nameof(BotStatusReason.ForeignOrder));
        HttpClient admin = await _factory.SignIn(await _factory.CreateAccount(UserRole.Owner));

        BotAlertResponse[] listed = (await owner.Client.GetFromJsonAsync<BotAlertResponse[]>("/api/alerts?unacknowledgedOnly=true", WebJson))!;
        BotAlertResponse[] ofBot = (await owner.Client.GetFromJsonAsync<BotAlertResponse[]>($"{BotApi.Path}/{id}/alerts", WebJson))!;
        BotAlertResponse[] ofOther = (await other.Client.GetFromJsonAsync<BotAlertResponse[]>("/api/alerts", WebJson))!;
        BotAlertResponse[] everyUser = (await admin.GetFromJsonAsync<BotAlertResponse[]>("/api/alerts?everyUser=true&limit=500", WebJson))!;
        HttpResponseMessage foreign = await other.Client.PostAsync($"/api/alerts/{alert}/acknowledge", content: null);
        HttpResponseMessage foreignBot = await other.Client.GetAsync($"{BotApi.Path}/{id}/alerts");
        var acknowledged =
            (await (await owner.Client.PostAsync($"/api/alerts/{alert}/acknowledge", content: null)).Content.ReadFromJsonAsync<BotAlertResponse>(WebJson))!;
        BotAlertResponse[] unread = (await owner.Client.GetFromJsonAsync<BotAlertResponse[]>("/api/alerts?unacknowledgedOnly=true", WebJson))!;

        listed.ShouldBe([new BotAlertResponse(alert, id, BotAlertKind.Degraded, "ForeignOrder", BotSeed.Now, AcknowledgedAt: null)]);
        ofBot.Select(item => item.Id).ShouldBe([alert]);
        ofOther.ShouldBeEmpty();
        everyUser.Select(item => item.Id).ShouldContain(alert);
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        foreignBot.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        acknowledged.AcknowledgedAt.ShouldNotBeNull();
        acknowledged.BotId.ShouldBe(id);
        acknowledged.Kind.ShouldBe(BotAlertKind.Degraded);
        acknowledged.Reason.ShouldBe("ForeignOrder");
        acknowledged.CreatedAt.ShouldBe(BotSeed.Now);
        unread.ShouldBeEmpty();
    }

    private async Task<Guid> RaiseAlert(Guid botId, BotAlertKind kind, string reason)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        Bot bot = (await scope.ServiceProvider.GetRequiredService<IBotRepository>().Get(new BotId(botId), CancellationToken.None))!;
        BotAlert alert = BotAlert.Raise(BotAlertId.New(), bot.Id, bot.OwnerId, kind, reason, BotSeed.Now);
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await scope.ServiceProvider.GetRequiredService<IBotAlertRepository>().Add(alert, CancellationToken.None);

                    return true;
                },
                CancellationToken.None);

        return alert.Id.Value;
    }
}