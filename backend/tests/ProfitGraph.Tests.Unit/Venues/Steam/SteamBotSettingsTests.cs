using System.Text.Json;

using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Venues.Steam.Trading;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     A Steam bot's own settings, checked and written in version 1 (ADR 0022, C9).
/// </summary>
public sealed class SteamBotSettingsTests
{
    private readonly SteamBotSettingsSchema _schema = new SteamBotSettingsSchema();

    [Test]
    public void MissingFieldsTakeTheirDefaults()
    {
        _schema.Defaults.ShouldBe(new BotVenueSettings(version: 1, json: """{"settlementHoldDays":7,"marketBuySlippage":"0","marketBuyDeadlineSeconds":120}"""));
        _schema.Read(Json("""{"marketBuySlippage":"0.02"}"""))
            .ShouldBe(new BotVenueSettings(version: 1, json: """{"settlementHoldDays":7,"marketBuySlippage":"0.02","marketBuyDeadlineSeconds":120}"""));
    }

    [Test]
    public void StoredSettingsReadBack()
    {
        BotVenueSettings stored = _schema.Read(Json("""{"settlementHoldDays":8,"marketBuySlippage":"0.1","marketBuyDeadlineSeconds":600}"""));

        SteamBotSettingsSchema.Parse(stored).ShouldBe(new SteamBotSettings(SettlementHoldDays: 8, MarketBuySlippage: 0.1m, MarketBuyDeadlineSeconds: 600));
        Should.Throw<InvalidOperationException>(() => SteamBotSettingsSchema.Parse(BotVenueSettings.None));
    }

    [TestCase(arg1: """{"marketBuySlippage":"0.11"}""", "VenueSettings.marketBuySlippage")]
    [TestCase(arg1: """{"marketBuySlippage":0.02}""", "VenueSettings.marketBuySlippage")]
    [TestCase(arg1: """{"marketBuySlippage":"-0.01"}""", "VenueSettings.marketBuySlippage")]
    [TestCase(arg1: """{"marketBuyDeadlineSeconds":5}""", "VenueSettings.marketBuyDeadlineSeconds")]
    [TestCase(arg1: """{"settlementHoldDays":31}""", "VenueSettings.settlementHoldDays")]
    [TestCase(arg1: """{"settlementHoldDays":1.5}""", "VenueSettings.settlementHoldDays")]
    [TestCase(arg1: """{"autoConfirm":true}""", "VenueSettings.autoConfirm")]
    [TestCase(arg1: """[1]""", "VenueSettings")]
    public void AnythingElseIsRefusedByField(string json, string field)
    {
        var error = Should.Throw<ApplicationValidationException>(() => _schema.Read(Json(json)));

        error.Errors.Select(item => item.Field).ShouldBe([field]);
    }

    private static JsonElement Json(string json)
    {
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}