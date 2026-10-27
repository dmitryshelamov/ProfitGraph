using ProfitGraph.Application.Bots.Runtime;
using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Tests.Unit.Application.Bots;

/// <summary>
///     A bot's failure reaches the log and the feed as a short line that never carries what the application did not write
///     itself (ADR 0029).
/// </summary>
public sealed class BotFailureDiagnosticsTests
{
    [Test]
    public void AnInvalidOperationOfOurOwnCodeKeepsItsMessage()
    {
        Exception exception = Thrown(() => throw new InvalidOperationException("Closed history cannot restore the bot's checkpoint."));

        BotFailureDiagnostics.Describe(exception).ShouldBe("InvalidOperationException: Closed history cannot restore the bot's checkpoint.");
    }

    [Test]
    public void ASafetyExceptionKeepsItsMessage()
    {
        var exception = new OrderGatewaySafetyException(BotStatusReason.HistoryDataCorrection, "The venue changed a closed candle.");

        BotFailureDiagnostics.Describe(exception).ShouldBe("OrderGatewaySafetyException: The venue changed a closed candle.");
    }

    [Test]
    public void AnExceptionOfAnotherTypeIsReducedToItsTypeName()
    {
        var exception = new HttpRequestException("GET https://api.example.test/v5/order?apiKey=SECRETSECRETSECRETSECRET failed.");

        BotFailureDiagnostics.Describe(exception).ShouldBe("HttpRequestException");
    }

    [Test]
    public void AnInvalidOperationFromTheFrameworkIsReducedToItsTypeName()
    {
        Exception exception = Thrown(() => _ = new List<int>().First());

        BotFailureDiagnostics.Describe(exception).ShouldBe("InvalidOperationException");
    }

    [Test]
    public void ALongTokenInAnOwnMessageIsMasked()
    {
        Exception exception = Thrown(() => throw new InvalidOperationException("Rejected key AbCdEfGhIjKlMnOpQrStUvWxYz0123456789 for the account."));

        BotFailureDiagnostics.Describe(exception).ShouldBe("InvalidOperationException: Rejected key *** for the account.");
    }

    [Test]
    public void AWrappedFailureIsDescribedLevelByLevel()
    {
        Exception exception = Thrown(
            () => throw new InvalidOperationException("A live input of the bot failed.", new HttpRequestException("secret detail")));

        BotFailureDiagnostics.Describe(exception).ShouldBe("InvalidOperationException: A live input of the bot failed. <- HttpRequestException");
    }

    private static Exception Thrown(Action action)
    {
        return Should.Throw<Exception>(action);
    }
}
