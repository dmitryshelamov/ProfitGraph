using System.Net;
using System.Net.Http.Json;

using Dapper;

using MediatR;

using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using ProfitGraph.Api.Scanner.Contracts;
using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Common.Transactions;
using ProfitGraph.Application.Notifications.DisableScanAlerts;
using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Application.Scanner.ReplaceScanSnapshot;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.Notifications;
using ProfitGraph.Engine.Scanning;
using ProfitGraph.Tests.Integration.Common;

namespace ProfitGraph.Tests.Integration.Scanner;

/// <summary>
///     A preset's Telegram alerts (ADR 0059, section 5): switched on through the API, the first snapshot only records the result, a
///     pair that enters the next one queues one scanner alert for the owner's linked chat, and the «Отключить» button switches the
///     alerts off for the chat's own user only.
/// </summary>
[NonParallelizable]
public sealed class ScanAlertsTests
{
    // Later than the snapshots other tests store, so theirs never count as newer than these.
    private static readonly DateTimeOffset First = DateTimeOffset.UtcNow.AddDays(1);

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
    public async Task APairThatEntersTheResultQueuesOneAlertUntilTheButtonSwitchesItOff()
    {
        UserInternal owner = await _api.CreateAccount(UserRole.User);
        using HttpClient client = await _api.SignIn(owner);
        using HttpClient stranger = await _api.CreateSignedInClient();
        await Link(owner, chatId: 7001);
        var request = new ScanPresetRequest("Алерт", [new ScanConditionRequest("Change1h", "3", Maximum: null)], "Change1h", SortDescending: true, HideStale: false);
        var preset = await (await client.PostAsJsonAsync("/api/scanner/bybit/presets", request)).Content.ReadFromJsonAsync<ScanPresetResponse>();
        Guid presetId = preset!.Id!.Value;

        HttpResponseMessage strangerSwitch = await stranger.PutAsJsonAsync($"/api/scanner/presets/{presetId}/alerts", new ScanPresetAlertsRequest(Enabled: true));
        HttpResponseMessage switched = await client.PutAsJsonAsync($"/api/scanner/presets/{presetId}/alerts", new ScanPresetAlertsRequest(Enabled: true));

        strangerSwitch.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await switched.Content.ReadFromJsonAsync<ScanPresetResponse>())!.AlertsEnabled.ShouldBeTrue();
        (await Presets(client)).Single(item => item.Id == presetId).AlertsEnabled.ShouldBeTrue();

        await Replace(First, Row("SOLUSDT", change1h: 4));
        (await Alerts(presetId)).ShouldBeEmpty();

        await Replace(First.AddMinutes(1), Row("SOLUSDT", change1h: 4), Row("HYPEUSDT", change1h: 5.5m), Row("BTCUSDT", change1h: 1));
        await Replace(First.AddMinutes(2), Row("SOLUSDT", change1h: 4), Row("HYPEUSDT", change1h: 6));

        string details = (await Alerts(presetId)).ShouldHaveSingleItem();
        details.ShouldContain("\"symbol.0\": \"HYPEUSDT\"");
        details.ShouldContain("\"count\": \"1\"");
        details.ShouldContain("Change1h=5.5");

        (await Send(new DisableScanAlertsCommand(ChatId: 7002, presetId))).ShouldBeFalse();
        (await Send(new DisableScanAlertsCommand(ChatId: 7001, presetId))).ShouldBeTrue();
        (await Send(new DisableScanAlertsCommand(ChatId: 7001, presetId))).ShouldBeFalse();
        (await Presets(client)).Single(item => item.Id == presetId).AlertsEnabled.ShouldBeFalse();

        await Replace(First.AddMinutes(3), Row("ETHUSDT", change1h: 9));
        (await Alerts(presetId)).Count.ShouldBe(1);
    }

    private static ScanSnapshotRowInternal Row(string symbol, decimal change1h)
    {
        return new ScanSnapshotRowInternal(symbol, new ScanMetrics { Price = 10, Change1h = change1h }, Incomplete: false);
    }

    private static async Task<List<ScanPresetResponse>> Presets(HttpClient client)
    {
        return (await client.GetFromJsonAsync<List<ScanPresetResponse>>("/api/scanner/bybit/presets"))!;
    }

    private static async Task<List<string>> Alerts(Guid presetId)
    {
        await using var connection = new NpgsqlConnection(TestDatabase.ConnectionString);

        return
        [
            .. await connection.QueryAsync<string>(
                "SELECT details::text FROM telegram_notifications WHERE scan_preset_id = @presetId AND kind = 'ScanAlert'",
                new { presetId })
        ];
    }

    private async Task Replace(DateTimeOffset computedAt, params ScanSnapshotRowInternal[] rows)
    {
        using IServiceScope scope = _api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISender>().Send(new ReplaceScanSnapshotCommand(new ScanSnapshotInternal("bybit", computedAt, rows)));
    }

    private async Task<T> Send<T>(IRequest<T> request)
    {
        using IServiceScope scope = _api.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    private async Task Link(UserInternal user, long chatId)
    {
        using IServiceScope scope = _api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>()
            .ExecuteAsync(
                async () =>
                {
                    await scope.ServiceProvider.GetRequiredService<ITelegramLinkRepository>()
                        .Save(TelegramLink.Link(new UserId(user.Id), chatId, DateTimeOffset.UtcNow.AddSeconds(-1)), CancellationToken.None);

                    return true;
                },
                CancellationToken.None);
    }
}