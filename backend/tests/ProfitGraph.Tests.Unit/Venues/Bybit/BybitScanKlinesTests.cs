using ProfitGraph.Application.Scanner.Contracts;
using ProfitGraph.Venues.Bybit.LiveData;
using ProfitGraph.Venues.Bybit.Scanner;

namespace ProfitGraph.Tests.Unit.Venues.Bybit;

public sealed class BybitScanKlinesTests
{
    private static readonly DateTimeOffset Minute = new DateTimeOffset(year: 2026, month: 10, day: 29, hour: 12, minute: 30, second: 0, TimeSpan.Zero);
    private static readonly HashSet<string> Symbols = ["BTCUSDT", "ETHUSDT"];

    [Test]
    public void ReadsClosedAndFormingMinutesWithTurnover()
    {
        string message = Message("kline.1.ETHUSDT", Row(Minute.AddMinutes(1), confirm: false), Row(Minute, confirm: true));

        IReadOnlyList<ScanCandleUpdateInternal> updates = BybitScanKlines.Read(message, Symbols);

        updates.Count.ShouldBe(2);
        updates[0].Symbol.ShouldBe("ETHUSDT");
        updates[0].Closed.ShouldBeTrue();
        updates[0].Candle.OpenTime.ShouldBe(Minute);
        updates[0].Candle.Turnover.ShouldBe(512.5m);
        updates[1].Closed.ShouldBeFalse();
    }

    [TestCase("kline.1.XRPUSDT")]
    [TestCase("kline.5.BTCUSDT")]
    [TestCase("tickers.BTCUSDT")]
    public void IgnoresTopicsItDidNotSubscribe(string topic)
    {
        BybitScanKlines.Read(Message(topic, Row(Minute, confirm: true)), Symbols).ShouldBeEmpty();
    }

    [Test]
    public void IgnoresSubscriptionAnswersAndPongs()
    {
        BybitScanKlines.Read(message: """{"success":true,"ret_msg":"","op":"subscribe","conn_id":"x"}""", Symbols).ShouldBeEmpty();
        BybitScanKlines.Read(message: """{"op":"pong"}""", Symbols).ShouldBeEmpty();
    }

    [Test]
    public void RejectsAMisalignedOrInvalidCandle()
    {
        Should.Throw<InvalidDataException>(() => BybitScanKlines.Read(Message("kline.1.BTCUSDT", Row(Minute.AddSeconds(1), confirm: true)), Symbols));
        Should.Throw<InvalidDataException>(() => BybitScanKlines.Read(Message("kline.1.BTCUSDT", Row(Minute, confirm: true, "-1")), Symbols));
    }

    [Test]
    public void SubscribesAtMostTenTopicsPerRequest()
    {
        string[] topics = [.. Enumerable.Range(start: 0, count: 25).Select(index => $"kline.1.T{index}USDT")];

        IReadOnlyList<string> messages = BybitPublicWebSocketTransport.SubscribeMessages(topics);

        messages.Count.ShouldBe(3);
        messages[0]
            .ShouldBe(
                """{"op":"subscribe","args":["kline.1.T0USDT","kline.1.T1USDT","kline.1.T2USDT","kline.1.T3USDT","kline.1.T4USDT","kline.1.T5USDT","kline.1.T6USDT","kline.1.T7USDT","kline.1.T8USDT","kline.1.T9USDT"]}""");
        messages[2].ShouldEndWith("""["kline.1.T20USDT","kline.1.T21USDT","kline.1.T22USDT","kline.1.T23USDT","kline.1.T24USDT"]}""");
    }

    private static string Message(string topic, params string[] rows)
    {
        return $$"""{"topic":"{{topic}}","type":"snapshot","ts":1,"data":[{{string.Join(",", rows)}}]}""";
    }

    private static string Row(DateTimeOffset openTime, bool confirm, string turnover = "512.5")
    {
        return $$"""
                 {"start":{{openTime.ToUnixTimeMilliseconds()}},"end":0,"interval":"1","open":"100","high":"110","low":"90","close":"105","volume":"5","turnover":"{{turnover}}","confirm":{{(confirm ? "true" : "false")}},"timestamp":1}
                 """;
    }
}