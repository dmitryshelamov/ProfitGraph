using System.Text.Json;

using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Bots;

/// <summary>
///     Bot settings of the test venue: one <c>level</c>, <c>low</c> or <c>high</c>.
/// </summary>
public sealed class FakeVenueBotSettings : IVenueBotSettings
{
    public VenueId Venue { get; } = new VenueId(TestHistoryVenue.WithHistory);

    public BotVenueSettings Defaults { get; } = new BotVenueSettings(version: 3, json: """{"level":"low"}""");

    public BotVenueSettings Read(JsonElement settings)
    {
        return settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty("level", out JsonElement level) &&
            level.GetString() is "low" or "high" && settings.EnumerateObject().Count() == 1
                ? new BotVenueSettings(version: 3, JsonSerializer.Serialize(new { level = level.GetString() }))
                : throw new ApplicationValidationException([new InputError("invalid_value", "VenueSettings.level", "The level is low or high.")]);
    }
}