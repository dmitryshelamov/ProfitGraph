using System.Text;

using ProfitGraph.Venues.Steam;
using ProfitGraph.Venues.Steam.MarketData;
using ProfitGraph.Venues.Steam.Orders;
using ProfitGraph.Venues.Steam.Session;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     Steam's write answers, buy-order counters and price texts, as Profit Fox Designer read them.
/// </summary>
public sealed class SteamOrderRepliesTests
{
    private static readonly SteamCurrency Kzt = SteamCurrency.Find("KZT")!;
    private static readonly SteamCurrency Usd = SteamCurrency.Find("USD")!;

    [Test]
    public void AnAcceptedBuyCarriesItsSteamId()
    {
        SteamWriteReceipt receipt = Read(SteamRequestOutcome.Success, body: """{"success":1,"buy_orderid":"5012345678"}""");

        receipt.Kind.ShouldBe(SteamWriteReceiptKind.Accepted);
        receipt.SteamOrderId.ShouldBe("5012345678");
    }

    [Test]
    public void AConfirmationIsReadBeforeSuccessEvenFromAClientError()
    {
        SteamWriteReceipt receipt = Read(SteamRequestOutcome.ClientError, body: """{"success":22,"need_confirmation":true,"confirmation":{"confirmation_id":4242}}""");

        receipt.Kind.ShouldBe(SteamWriteReceiptKind.NeedsConfirmation);
        receipt.ConfirmationCreator.ShouldBe("4242");
    }

    [Test]
    public void OnlyAConfirmedCodeRejects()
    {
        Read(SteamRequestOutcome.Success, body: """{"success":29}""").Kind.ShouldBe(SteamWriteReceiptKind.Unknown);
        Read(SteamRequestOutcome.Success, body: """{"success":29}""", [29]).ShouldBe(new SteamWriteReceipt(SteamWriteReceiptKind.Rejected, SteamCode: 29));
        Read(SteamRequestOutcome.ClientError, body: """{"success":1,"buy_orderid":"1"}""").Kind.ShouldBe(SteamWriteReceiptKind.Unknown);
    }

    [TestCase("RateLimited")]
    [TestCase("Unknown")]
    [TestCase("Unauthorized")]
    [TestCase("Captcha")]
    public void AnythingButAClearAnswerIsUnknown(string outcome)
    {
        Read(Enum.Parse<SteamRequestOutcome>(outcome), body: """{"success":1,"buy_orderid":"1"}""").Kind.ShouldBe(SteamWriteReceiptKind.Unknown);
        Read(SteamRequestOutcome.Success, "not json").Kind.ShouldBe(SteamWriteReceiptKind.Unknown);
        Read(SteamRequestOutcome.Success, string.Empty).Kind.ShouldBe(SteamWriteReceiptKind.Unknown);
    }

    [TestCase("NotSent")]
    [TestCase("Deferred")]
    public void OnlyARequestThatNeverLeftIsUnsent(string outcome)
    {
        SteamWriteReplyParser.Read(new SteamRequestResult(Enum.Parse<SteamRequestOutcome>(outcome)), "buy_orderid", []).Kind.ShouldBe(SteamWriteReceiptKind.NotSent);
    }

    [Test]
    public void ReadsBuyOrderCountersThatAddUp()
    {
        SteamBuyOrderStatusParser.Read(Encoding.UTF8.GetBytes("""{"success":1,"active":0,"purchased":1,"quantity":"3","quantity_remaining":"2"}"""))
            .ShouldBe(new SteamBuyOrderStatus(Active: false, Quantity: 3, Remaining: 2));
        SteamBuyOrderStatusParser.Read(Encoding.UTF8.GetBytes("""{"success":1,"active":1,"quantity":3,"quantity_remaining":4}""")).ShouldBeNull();
        SteamBuyOrderStatusParser.Read(Encoding.UTF8.GetBytes("""{"success":8}""")).ShouldBeNull();
    }

    [TestCase("615,00₸", arg2: 61500L)]
    [TestCase("1 234,56₸", arg2: 123456L)]
    [TestCase("12₸", arg2: 1200L)]
    public void ReadsTengePrices(string text, long minorUnits)
    {
        SteamPriceText.MinorUnits(text, Kzt).ShouldBe(minorUnits);
    }

    [TestCase("615,00₸", arg2: null)]
    [TestCase("$6.15", arg2: 615L)]
    [TestCase("$1,234.50", arg2: 123450L)]
    public void ReadsDollarPrices(string text, long? minorUnits)
    {
        SteamPriceText.MinorUnits(text, Usd).ShouldBe(minorUnits);
    }

    [TestCase("6 15,00₸")]
    [TestCase("12,3,4₸")]
    [TestCase("1 2345,00₸")]
    [TestCase("-5,00₸")]
    [TestCase("615,00")]
    public void AnythingElseIsUnreadable(string text)
    {
        SteamPriceText.MinorUnits(text, Kzt).ShouldBeNull();
    }

    private static SteamWriteReceipt Read(SteamRequestOutcome outcome, string body, int[]? rejectionCodes = null)
    {
        var response = new SteamHttpResponse(StatusCode: 200, Encoding.UTF8.GetBytes(body), "application/json", [], Location: null);

        return SteamWriteReplyParser.Read(new SteamRequestResult(outcome, response), "buy_orderid", rejectionCodes ?? []);
    }
}