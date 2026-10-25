using ProfitGraph.Domain.Bots;

namespace ProfitGraph.Tests.Unit.Domain.Bots;

/// <summary>
///     A bot keeps its venue module's settings as a versioned JSON object of bounded size (ADR 0022, C9).
/// </summary>
public sealed class BotVenueSettingsTests
{
    [TestCase(arg1: -1, "{}")]
    [TestCase(arg1: 0, "[]")]
    [TestCase(arg1: 0, "")]
    public void VenueSettingsAreAVersionedObject(int version, string json)
    {
        Should.Throw<BotRuleException>(() => new BotVenueSettings(version, json));
        Should.Throw<BotRuleException>(() => new BotVenueSettings(version: 1, "{" + new string(c: ' ', BotVenueSettings.MaxLength) + "}"));
    }
}