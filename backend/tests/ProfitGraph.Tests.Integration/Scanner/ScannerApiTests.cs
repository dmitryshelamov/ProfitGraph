using System.Net;
using System.Net.Http.Json;

using MediatR;

using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.ReplaceScanSnapshot;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Scanner;

/// <summary>
///     The scanner over HTTP (ADR 0059): the venues that have one, a venue's snapshot as exact decimal strings, Bybit's built-in
///     presets, and a user's own presets, which no other user sees or changes.
/// </summary>
[NonParallelizable]
public sealed class ScannerApiTests
{
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
    public async Task ListsBybitAndRefusesAVenueWithoutAScanner()
    {
        using HttpClient client = await _api.CreateSignedInClient();

        var venues = await client.GetFromJsonAsync<List<ScanVenueResponse>>("/api/scanner/venues");
        HttpResponseMessage unknown = await client.GetAsync("/api/scanner/nowhere/snapshot");

        venues!.Select(venue => venue.Venue).ShouldContain("bybit");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknown.Content.ReadAsStringAsync()).ShouldContain("scanner_venue_not_found");
    }

    [Test]
    public async Task SnapshotCarriesExactDecimalStrings()
    {
        var computedAt = new DateTimeOffset(year: 2026, month: 10, day: 29, hour: 10, minute: 1, second: 5, TimeSpan.Zero);

        using (IServiceScope scope = _api.Services.CreateScope())
        {
            var row = new ScanSnapshotRowInternal("BTCUSDT", new ScanMetrics { Price = 100.25m, Change1h = -1.5m, ListingAgeDays = 3 }, Incomplete: true);
            await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ReplaceScanSnapshotCommand(new ScanSnapshotInternal("bybit", computedAt, [row])));
        }

        using HttpClient client = await _api.CreateSignedInClient();
        var snapshot = await client.GetFromJsonAsync<ScanSnapshotResponse>("/api/scanner/bybit/snapshot");

        snapshot!.ComputedAt.ShouldBe(computedAt);
        ScanSnapshotRowResponse btc = snapshot.Rows.Single();
        (btc.Symbol, btc.Price, btc.Change1h, btc.Change5m, btc.ListingAgeDays, btc.Incomplete).ShouldBe(("BTCUSDT", "100.25", "-1.5", (string?)null, (int?)3, true));
    }

    [Test]
    public async Task OwnPresetsFollowTheBuiltInOnesAndStayTheOwners()
    {
        using HttpClient owner = await _api.CreateSignedInClient();
        using HttpClient stranger = await _api.CreateSignedInClient();
        var request = new ScanPresetRequest("Мой", [new ScanConditionRequest("Change1h", "2.5", Maximum: null)], "Change1h", SortDescending: true, HideStale: true);

        HttpResponseMessage created = await owner.PostAsJsonAsync("/api/scanner/bybit/presets", request);
        var preset = await created.Content.ReadFromJsonAsync<ScanPresetResponse>();
        HttpResponseMessage duplicate = await owner.PostAsJsonAsync("/api/scanner/bybit/presets", request with { Name = "мой" });
        var ownList = await owner.GetFromJsonAsync<List<ScanPresetResponse>>("/api/scanner/bybit/presets");
        var strangerList = await stranger.GetFromJsonAsync<List<ScanPresetResponse>>("/api/scanner/bybit/presets");
        HttpResponseMessage strangerChange = await stranger.PutAsJsonAsync($"/api/scanner/presets/{preset!.Id}", request with { Name = "Чужой" });

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        preset.Conditions.Single().ShouldBe(new ScanConditionResponse("Change1h", "2.5", Maximum: null));
        preset.TradeDirection.ShouldBe("Both");
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        ownList!.Select(item => (item.Name, item.BuiltIn)).ShouldBe(
            [("Импульс", true), ("Ранний импульс", true), ("Падение", true), ("Раннее падение", true), ("Под лесенку", true), ("Мой", false)]);
        strangerList!.ShouldAllBe(item => item.BuiltIn);
        ownList!.Where(item => item.BuiltIn).Select(item => item.TradeDirection).ShouldBe(["Long", "Long", "Short", "Short", "Both"]);
        strangerChange.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        HttpResponseMessage changed = await owner.PutAsJsonAsync(
            $"/api/scanner/presets/{preset.Id}",
            request with { Name = "Мой узкий", Conditions = [new ScanConditionRequest("Spread", Minimum: null, "0.05")], TradeDirection = "Short" });
        HttpResponseMessage wrongDirection = await owner.PutAsJsonAsync($"/api/scanner/presets/{preset.Id}", request with { TradeDirection = "Up" });
        var stored = await owner.GetFromJsonAsync<List<ScanPresetResponse>>("/api/scanner/bybit/presets");
        HttpResponseMessage deleted = await owner.DeleteAsync($"/api/scanner/presets/{preset.Id}");
        HttpResponseMessage deletedAgain = await owner.DeleteAsync($"/api/scanner/presets/{preset.Id}");

        (await changed.Content.ReadFromJsonAsync<ScanPresetResponse>())!.Conditions.Single().ShouldBe(new ScanConditionResponse("Spread", Minimum: null, "0.05"));
        wrongDirection.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        stored!.Single(item => !item.BuiltIn).TradeDirection.ShouldBe("Short");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        deletedAgain.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TheHubTellsEveryClientAboutANewSnapshot()
    {
        UserInternal user = await _api.CreateAccount(UserRole.User);
        string cookie = await _api.SessionCookie(user);
        HubConnection connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(_api.Server.BaseAddress, "/ws/scanner"),
                options =>
                {
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => _api.Server.CreateHandler();
                    options.Headers["Cookie"] = cookie;
                })
            .Build();
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On("ScanSnapshotChanged", () => changed.TrySetResult());
        await connection.StartAsync();

        try
        {
            // The publisher may start listening after the first snapshot, so snapshots are stored until one is heard.
            for (var attempt = 0; attempt < 50 && !changed.Task.IsCompleted; attempt++)
            {
                using IServiceScope scope = _api.Services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ISender>()
                    .Send(new ReplaceScanSnapshotCommand(new ScanSnapshotInternal("bybit", DateTimeOffset.UtcNow, [])));
                await Task.WhenAny(changed.Task, Task.Delay(millisecondsDelay: 200));
            }

            changed.Task.IsCompleted.ShouldBeTrue();
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }

    [TestCase("Unknown", "1", null, HttpStatusCode.BadRequest)]
    [TestCase("Change1h", "1e3", null, HttpStatusCode.BadRequest)]
    [TestCase("Change1h", null, null, HttpStatusCode.UnprocessableEntity)]
    [TestCase("Change1h", "2", "1", HttpStatusCode.UnprocessableEntity)]
    public async Task RefusesAnInvalidCondition(string metric, string? minimum, string? maximum, HttpStatusCode expected)
    {
        using HttpClient client = await _api.CreateSignedInClient();
        var request = new ScanPresetRequest("Плохой", [new ScanConditionRequest(metric, minimum, maximum)], "Change1h", SortDescending: true, HideStale: false);

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/scanner/bybit/presets", request);

        response.StatusCode.ShouldBe(expected);
    }
}