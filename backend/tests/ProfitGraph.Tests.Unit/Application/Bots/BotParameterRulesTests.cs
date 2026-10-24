using ProfitGraph.Application.Bots;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Engine.Templates;

namespace ProfitGraph.Tests.Unit.Application.Bots;

public sealed class BotParameterRulesTests
{
    [Test]
    public void ValuesForEveryParameterPass()
    {
        Should.NotThrow(() => BotParameterRules.Ensure(
            SmaStrategyBlockFactory.Create(),
            new Dictionary<string, decimal> { ["fastPeriod"] = 2m, ["slowPeriod"] = 4m, ["buyAmount"] = 10.5m }));
    }

    [Test]
    public void MissingUnknownAndFractionalIntegerValuesAreReportedTogether()
    {
        var invalid = Should.Throw<ApplicationValidationException>(() => BotParameterRules.Ensure(
            SmaStrategyBlockFactory.Create(),
            new Dictionary<string, decimal> { ["fastPeriod"] = 2.5m, ["buyAmount"] = 10m, ["extra"] = 1m }));

        invalid.Errors.Select(error => error.Field).ShouldBe(["Parameters.fastPeriod", "Parameters.slowPeriod", "Parameters.extra"], ignoreOrder: true);
        invalid.Errors.ShouldAllBe(error => error.Code == BotErrorCodes.ParametersMismatch);
    }
}