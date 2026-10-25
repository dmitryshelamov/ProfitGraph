using System.Globalization;

using Microsoft.Extensions.Options;

using ProfitGraph.Application.Accounts.Contracts;
using ProfitGraph.Application.Bots;
using ProfitGraph.Application.Bots.Contracts;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Engine.Market;
using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.LiveData;
using ProfitGraph.Venues.Steam.MarketData;
using ProfitGraph.Venues.Steam.Trading;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     A bot's wallet fees, live median candles and the display price, against the mock Steam on a fake clock.
/// </summary>
public sealed class SteamLiveDataTests
{
    private static readonly InstrumentSymbol Case = new InstrumentSymbol("KZT:730:Dreams & Nightmares Case");
    private static readonly Timeframe Day = Timeframe.Parse("1d");

    [Test]
    public async Task ABotsFeesAreItsWalletsSellerFees()
    {
        var harness = new SteamSessionHarness();
        var reader = new SteamFeeRateReader(harness.MarketData());
        var secrets = new ExchangeCredentialSecretsInternal(SteamSessionHarness.Credential, "steam", new Dictionary<string, string>());

        VenueFeeRatesInternal rates = await reader.Read(secrets, Case, CancellationToken.None);

        rates.MakerRate.ShouldBe(0m);
        rates.TakerRate.ShouldBe(0m);
        rates.SellerFees!.Rates.ShouldBe([0.05m, 0.10m]);
        (await Should.ThrowAsync<VenueFeeRatesUnavailableException>(() => reader.Read(
                secrets,
                new InstrumentSymbol("USD:730:Dreams & Nightmares Case"),
                CancellationToken.None)))
            .Message.ShouldContain("KZT");
    }

    [Test]
    public async Task AnAccountIsWorthItsWalletInTheWalletsCurrency()
    {
        var harness = new SteamSessionHarness();
        var reader = new SteamAccountBalanceReader(harness.MarketData(), harness.Clock);

        AccountBalanceReadInternal read = await reader.Read(
            new ExchangeCredentialSecretsInternal(SteamSessionHarness.Credential, "steam", new Dictionary<string, string>()),
            CancellationToken.None);

        read.Observation!.TotalValue.ShouldBe(1234.56m);
        read.Observation.ValueCurrency.ShouldBe("KZT");
        read.Observation.Coins.ShouldBe([new AccountCoinBalanceInternal("KZT", Balance: 1234.56m, UsdValue: null)]);
    }

    [Test]
    public async Task TheDisplayPriceIsTheHighestBuyOrder()
    {
        var harness = new SteamSessionHarness();
        var source = new SteamLiveMarkPriceSource(harness.MarketData(), Options.Create(harness.Options), harness.Clock);
        using var stop = new CancellationTokenSource();

        await foreach (LiveMarkPriceInternal price in source.Subscribe(Case, stop.Token))
        {
            price.Price.ShouldBe(512.34m);
            price.ObservedAt.ShouldBe(harness.Clock.GetUtcNow());

            break;
        }

        harness.Steam.OrderBook = """{"data":{"success":1,"data":{"eCurrency":1,"amtMaxBuyOrder":100}}}""";
        await Should.ThrowAsync<SteamMarketDataException>(() => harness.MarketData().HighestBuyOrder(SteamInstrument(), CancellationToken.None));
        harness.Steam.OrderBook = """{"data":{"success":1,"data":{"eCurrency":37,"amtMaxBuyOrder":null}}}""";
        (await harness.MarketData().HighestBuyOrder(SteamInstrument(), CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task LiveCandlesFollowTheLastOneAndAGapFailsTheFeed()
    {
        var harness = new SteamSessionHarness();
        var today = new DateTimeOffset(harness.Clock.GetUtcNow().Date, TimeSpan.Zero);
        harness.Steam.PriceHistory = History(today.AddDays(-60), today.AddDays(-59), today.AddDays(-58), today.AddDays(-56));
        var source = new SteamLiveCandleSource(harness.MarketData(), Options.Create(harness.Options), harness.Clock);
        var received = new List<DateTimeOffset>();

        Exception? failure = await Record(async () =>
        {
            await foreach (Candle candle in source.Subscribe(Case, Day, today.AddDays(-60), CancellationToken.None))
            {
                received.Add(candle.OpenTime);
            }
        });

        received.ShouldBe([today.AddDays(-59), today.AddDays(-58)]);
        failure.ShouldBeOfType<InvalidOperationException>().Message.ShouldContain("no 1d candle");
    }

    private static SteamInstrument SteamInstrument()
    {
        ProfitGraph.Venues.Steam.SteamInstrument.TryParse(Case.Value, out SteamInstrument? instrument).ShouldBeTrue();

        return instrument!;
    }

    private static string History(params DateTimeOffset[] days)
    {
        return """{"success":true,"price_prefix":"","price_suffix":"₸","prices":[""" +
            string.Join(separator: ',', days.Select(day => string.Create(CultureInfo.InvariantCulture, $"[\"{day.AddHours(1):MMM dd yyyy HH}: +0\",500,\"10\"]"))) + "]}";
    }

    private static async Task<Exception?> Record(Func<Task> action)
    {
        try
        {
            await action();

            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}