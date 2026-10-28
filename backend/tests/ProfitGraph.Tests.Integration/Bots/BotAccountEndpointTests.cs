using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Api.Accounts.Contracts;
using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Accounts.RecordAccountBalance;
using ProfitGraph.Application.Accounts.RecordAccountIdentity;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Tests.Integration.Common;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     The account summary of the bot page over HTTP (ADR 0049), on the test venue with invented credentials: the latest
///     snapshot and the cached identity, a bot without a snapshot, a failed read, and another user's bot answering 404. No
///     response carries the key.
/// </summary>
[NonParallelizable]
public sealed class BotAccountEndpointTests
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
    public async Task SummaryShowsTheLatestSnapshotTheIdentityAndTheKeysOtherRunningBots()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        Guid other = await BotApi.Create(owner);
        (await owner.Client.PostAsync($"{BotApi.Path}/{other}/start", content: null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        DateTimeOffset latest = DateTimeOffset.UtcNow.AddMinutes(-1);
        await Send(new RecordAccountBalanceCommand(owner.CredentialId, Observed(totalValue: 900m, latest.AddMinutes(-10))));
        await Send(new RecordAccountBalanceCommand(owner.CredentialId, Observed(totalValue: 1234.5m, latest)));
        await Send(
            new RecordAccountIdentityCommand(
                owner.CredentialId,
                AccountIdentityReadInternal.Observed(
                    new AccountIdentityInternal("24617703", "invented", "main", "read_write", ReadOnly: false, ["Spot:SpotTrade"], ExpiresAt: null))));

        HttpResponseMessage response = await owner.Client.GetAsync($"{BotApi.Path}/{id}/account");
        string body = await response.Content.ReadAsStringAsync();
        var account = JsonSerializer.Deserialize<BotAccountResponse>(body, WebJson)!;

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldNotContain(BotApi.ApiKey);
        body.ShouldNotContain("test-api-secret-not-real");
        account.ShouldSatisfyAllConditions(
            () => (account.CredentialId, account.Venue, account.CredentialLabel, account.ApiKeyLast4).ShouldBe(
                (owner.CredentialId, TestHistoryVenue.WithHistory, "invented", "real")),
            () => account.AccountPublicId.ShouldBeNull(),
            () => account.Snapshot.ShouldNotBeNull().TotalValue.ShouldBe("1234.5"),
            () => account.Snapshot!.AvailableValue.ShouldBe("1000"),
            () => account.Snapshot!.CapturedAt.ShouldBe(latest, TimeSpan.FromMilliseconds(1)),
            () => account.Snapshot!.Coins.ShouldBe([new AccountCoinBalanceResponse("USDT", "1000", "1000")]),
            () => (account.FailureReason, account.LastSuccessAt is null).ShouldBe((null, false)),
            () => account.Identity.ShouldNotBeNull().AccountId.ShouldBe("24617703"),
            () => account.Identity!.Permissions.ShouldBe(["Spot:SpotTrade"]),
            () => account.IdentityFailureReason.ShouldBeNull(),
            () => account.Protection.Open.ShouldBeFalse(),
            () => account.AllocationCap.ShouldBe("100"),
            () => account.OtherRunningBots.ShouldHaveSingleItem()
                .ShouldSatisfyAllConditions(
                    bot => bot.BotId.ShouldBe(other),
                    bot => bot.Symbol.ShouldBe("BTCUSDT"),
                    bot => bot.AllocationCap.ShouldBe("100")));
    }

    [Test]
    public async Task BotWithoutASnapshotOrIdentityHasNone()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);

        var account = (await owner.Client.GetFromJsonAsync<BotAccountResponse>($"{BotApi.Path}/{id}/account", WebJson))!;

        account.ShouldSatisfyAllConditions(
            () => account.Snapshot.ShouldBeNull(),
            () => (account.LastAttemptAt, account.LastSuccessAt, account.FailureReason, account.NextAttemptAt).ShouldBe((null, null, null, null)),
            () => (account.Identity, account.IdentityFailureReason).ShouldBe((null, null)),
            () => account.OtherRunningBots.ShouldBeEmpty());
    }

    [Test]
    public async Task FailedReadsKeepTheLastSnapshotAndIdentityAndNameTheirReason()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        await Send(new RecordAccountBalanceCommand(owner.CredentialId, Observed(totalValue: 50m, DateTimeOffset.UtcNow.AddMinutes(-1))));
        await Send(new RecordAccountBalanceCommand(owner.CredentialId, AccountBalanceReadInternal.Failed("venue_unavailable", "503")));
        await Send(
            new RecordAccountIdentityCommand(
                owner.CredentialId,
                AccountIdentityReadInternal.Observed(new AccountIdentityInternal("1", Name: null, Kind: null, AccessLevel: null, ReadOnly: null, [], ExpiresAt: null))));
        await Send(new RecordAccountIdentityCommand(owner.CredentialId, AccountIdentityReadInternal.Failed("venue_rejected", "10003")));

        var account = (await owner.Client.GetFromJsonAsync<BotAccountResponse>($"{BotApi.Path}/{id}/account", WebJson))!;

        account.ShouldSatisfyAllConditions(
            () => account.Snapshot.ShouldNotBeNull().TotalValue.ShouldBe("50"),
            () => account.FailureReason.ShouldBe("venue_unavailable"),
            () => account.LastSuccessAt.ShouldNotBeNull(),
            () => account.NextAttemptAt.ShouldNotBeNull(),
            () => account.Identity.ShouldNotBeNull().AccountId.ShouldBe("1"),
            () => account.IdentityFailureReason.ShouldBe("venue_rejected"));
    }

    [Test]
    public async Task AnotherUsersBotAndAnUnknownBotAreMissing()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        Guid id = await BotApi.Create(owner);
        BotOwner stranger = await BotApi.NewOwner(_factory);

        HttpResponseMessage foreign = await stranger.Client.GetAsync($"{BotApi.Path}/{id}/account");
        HttpResponseMessage unknown = await owner.Client.GetAsync($"{BotApi.Path}/{Guid.NewGuid()}/account");

        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await BotApi.Code(foreign)).ShouldBe("bot_not_found");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task IdentityIsDueForVenuesWithAReaderUntilItIsRead_AndAgainAfterAFailure()
    {
        BotOwner owner = await BotApi.NewOwner(_factory);
        await BotApi.Create(owner);
        var venue = new VenueId(TestHistoryVenue.WithHistory);
        var credential = new ExchangeCredentialId(owner.CredentialId);

        (await Due([venue], DateTimeOffset.UtcNow)).ShouldContain(due => due.CredentialId == credential);
        (await Due([new VenueId("elsewhere")], DateTimeOffset.UtcNow)).ShouldNotContain(due => due.CredentialId == credential);
        await Send(
            new RecordAccountIdentityCommand(
                owner.CredentialId,
                AccountIdentityReadInternal.Observed(new AccountIdentityInternal("1", Name: null, Kind: null, AccessLevel: null, ReadOnly: null, [], ExpiresAt: null))));
        (await Due([venue], DateTimeOffset.UtcNow)).ShouldNotContain(due => due.CredentialId == credential);
        (await Due([venue], DateTimeOffset.UtcNow.AddDays(1).AddMinutes(1))).ShouldContain(due => due.CredentialId == credential);
        await Send(new RecordAccountIdentityCommand(owner.CredentialId, AccountIdentityReadInternal.Failed("venue_unavailable")));
        (await Due([venue], DateTimeOffset.UtcNow.AddMinutes(61))).ShouldContain(due => due.CredentialId == credential);
    }

    private static AccountBalanceReadInternal Observed(decimal totalValue, DateTimeOffset at)
    {
        return AccountBalanceReadInternal.Observed(
            new AccountBalanceObservationInternal(
                totalValue,
                "USD",
                AvailableValue: 1000m,
                [new AccountCoinBalanceInternal("USDT", Balance: 1000m, UsdValue: 1000m)],
                at));
    }

    private async Task Send(IRequest command)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
    }

    private async Task Send<TResponse>(IRequest<TResponse> command)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
    }

    private async Task<IReadOnlyList<DueAccountIdentity>> Due(IReadOnlyList<VenueId> venues, DateTimeOffset now)
    {
        using IServiceScope scope = _factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IAccountIdentityRepository>().ListDue(venues, now, CancellationToken.None);
    }
}