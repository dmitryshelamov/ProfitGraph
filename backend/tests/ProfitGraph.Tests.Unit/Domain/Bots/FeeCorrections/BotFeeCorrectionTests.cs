using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.Bots.FeeCorrections;
using ProfitGraph.Domain.Bots.Fills;
using ProfitGraph.Engine.Accounting;

namespace ProfitGraph.Tests.Unit.Domain.Bots.FeeCorrections;

/// <summary>
///     A late commission is recorded as a correction beside the fill (ADR 0023, TC4), and a fill knows the asset of its fee.
/// </summary>
public sealed class BotFeeCorrectionTests
{
    private static readonly DateTimeOffset At = new DateTimeOffset(year: 2026, month: 10, day: 27, hour: 10, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void ACorrectionRaisesTheFee_AndIsReported()
    {
        BotFeeCorrection correction = BotFeeCorrection.Record(BotFeeCorrectionId.New(), BotId.New(), engineOrderId: 7, previousFee: 0m, correctedFee: 1.5m, At);

        correction.Delta.ShouldBe(1.5m);
        correction.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<BotFeeCorrected>().CorrectedFee.ShouldBe(1.5m);
    }

    [TestCase(arg1: 1.5, arg2: 1.5)]
    [TestCase(arg1: 1.5, arg2: 1.0)]
    [TestCase(arg1: -0.1, arg2: 1.0)]
    public void OnlyARaiseOfANonnegativeFeeIsACorrection(decimal previous, decimal corrected)
    {
        Should.Throw<ArgumentException>(() => BotFeeCorrection.Record(BotFeeCorrectionId.New(), BotId.New(), engineOrderId: 7, previous, corrected, At));
    }

    [Test]
    public void AFillKeepsTheAssetItsFeeWasPaidIn_AndOtherwiseFollowsTheSide()
    {
        BotFill quote = Fill(OrderSide.Buy, FeeAsset.Quote);
        BotFill buy = Fill(OrderSide.Buy, feeAsset: null);
        BotFill sell = Fill(OrderSide.Sell, feeAsset: null);

        quote.FeeAsset.ShouldBe(FeeAsset.Quote);
        buy.FeeAsset.ShouldBe(FeeAsset.Base);
        sell.FeeAsset.ShouldBe(FeeAsset.Quote);
    }

    private static BotFill Fill(OrderSide side, FeeAsset? feeAsset)
    {
        return BotFill.Record(
            BotFillId.New(),
            BotId.New(),
            engineOrderId: 1,
            side,
            tag: null,
            grossBase: 10m,
            grossQuote: 1200m,
            fee: 0.6m,
            cumulativeGrossBase: 10m,
            At,
            feeAsset);
    }
}