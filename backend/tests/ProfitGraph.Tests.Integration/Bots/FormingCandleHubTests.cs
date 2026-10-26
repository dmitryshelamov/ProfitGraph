using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Api.Bots.Contracts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     The forming candle of a bot's series over <c>/ws/forming-candles</c> (ADR 0026) on the test venue: only the bot's owner
///     follows it, clients of one series share one venue connection, and the connection closes with the last client. Values are
///     invented; no venue is contacted.
/// </summary>
[NonParallelizable]
public sealed class FormingCandleHubTests
{
    private static readonly DateTimeOffset Open = new DateTimeOffset(year: 2026, month: 10, day: 26, hour: 9, minute: 0, second: 0, TimeSpan.Zero);

    private ProfitGraphApiFactory _factory = null!;
    private FakeFormingCandleSource _venue = null!;

    [OneTimeSetUp]
    public void StartApi()
    {
        _factory = new ProfitGraphApiFactory(
            BotApi.Settings(),
            services: services => services.AddSingleton<FakeFormingCandleSource>()
                .AddSingleton<IFormingCandleSource>(provider => provider.GetRequiredService<FakeFormingCandleSource>()));
        _venue = _factory.Services.GetRequiredService<FakeFormingCandleSource>();
    }

    [OneTimeTearDown]
    public async Task StopApi()
    {
        await _factory.DisposeAsync();
    }

    [Test]
    public async Task TheOwnerReceivesTheFormingCandleAsExactStrings_AndAnotherUserIsRefusedAsMissing()
    {
        (UserInternal owner, Guid botId) = await NewBot();
        UserInternal other = await _factory.CreateAccount(UserRole.User);
        await using HubConnection ownerConnection = await Connect(owner);
        await using HubConnection otherConnection = await Connect(other);
        var received = new TaskCompletionSource<FormingCandleResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var live = new TaskCompletionSource<FormingStatusResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        ownerConnection.On<FormingCandleResponse>("FormingCandle", candle => received.TrySetResult(candle));
        ownerConnection.On<FormingStatusResponse>("FormingStatus", status => live.TrySetResult(status));

        var snapshot = await ownerConnection.InvokeAsync<FormingFeedSnapshotResponse>("Subscribe", botId);
        var refused = await Should.ThrowAsync<HubException>(() => otherConnection.InvokeAsync<FormingFeedSnapshotResponse>("Subscribe", botId));
        await Until(() => _venue.Open == 1);
        _venue.Push(new FormingCandleInternal(Open, Open: 100.5m, High: 110m, Low: 90m, Close: 105.25m, Volume: 5m, IsClosed: false, Open.AddSeconds(3)));

        FormingCandleResponse candle = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        FormingStatusResponse status = await live.Task.WaitAsync(TimeSpan.FromSeconds(10));
        snapshot.Status.ShouldBe("NoData");
        snapshot.Candle.ShouldBeNull();
        refused.Message.ShouldContain("bot_not_found");
        status.Status.ShouldBe("Live");
        (candle.OpenTime, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume, candle.IsClosed)
            .ShouldBe((Open, "100.5", "110", "90", "105.25", "5", false));
    }

    [Test]
    public async Task ClientsOfOneSeriesShareOneVenueConnection_AndTheLastToDisconnectClosesIt()
    {
        (UserInternal owner, Guid botId) = await NewBot();
        int before = _venue.Subscriptions;
        HubConnection first = await Connect(owner);
        HubConnection second = await Connect(owner);

        await first.InvokeAsync<FormingFeedSnapshotResponse>("Subscribe", botId);
        await second.InvokeAsync<FormingFeedSnapshotResponse>("Subscribe", botId);
        await Until(() => _venue.Open == 1);

        _venue.Subscriptions.ShouldBe(before + 1);

        await first.DisposeAsync();
        await Task.Delay(200);
        _venue.Open.ShouldBe(1);

        await second.DisposeAsync();
        await Until(() => _venue.Open == 0);
    }

    [Test]
    public async Task UnsubscribingClosesTheVenueConnection()
    {
        (UserInternal owner, Guid botId) = await NewBot();
        await using HubConnection connection = await Connect(owner);
        await connection.InvokeAsync<FormingFeedSnapshotResponse>("Subscribe", botId);
        await Until(() => _venue.Open == 1);

        await connection.InvokeAsync("Unsubscribe");

        await Until(() => _venue.Open == 0);
    }

    private async Task<(UserInternal Owner, Guid BotId)> NewBot()
    {
        UserInternal user = await _factory.CreateAccount(UserRole.User);
        BotOwner owner = await BotApi.NewOwner(_factory, account: user);
        string symbol = "F" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant() + "USDT";
        HttpResponseMessage created = await owner.Client.PostAsJsonAsync(BotApi.Path, BotApi.Request(owner, symbol: symbol));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        return (user, (await BotApi.Json(created)).GetProperty("id").GetGuid());
    }

    private async Task<HubConnection> Connect(UserInternal user)
    {
        string cookie = await _factory.SessionCookie(user);
        HubConnection connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(_factory.Server.BaseAddress, "/ws/forming-candles"),
                options =>
                {
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                    options.Headers["Cookie"] = cookie;
                })
            .Build();
        await connection.StartAsync();

        return connection;
    }

    private static async Task Until(Func<bool> condition)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);

        while (!condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("The expected state was not reached.");
            }

            await Task.Delay(20);
        }
    }
}