using System.Globalization;
using System.Text.Json;

using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;
using ProfitGraph.Tests.Integration.MarketData;

namespace ProfitGraph.Tests.Integration.Scanner;

/// <summary>
///     Bot settings of the test venue as a margin venue (ADR 0061): one <c>leverage</c>, a decimal string.
/// </summary>
public sealed class FakeLeverageSettings : IVenueBotSettings, IVenueLeverage
{
    public VenueId Venue { get; } = new VenueId(TestHistoryVenue.WithHistory);

    public BotVenueSettings Defaults { get; } = Write(leverage: 1m);

    public BotVenueSettings Read(JsonElement settings)
    {
        return settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty("leverage", out JsonElement leverage) &&
            decimal.TryParse(leverage.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value) && value >= 1m
                ? Write(value)
                : throw new ApplicationValidationException([new InputError("invalid_value", "VenueSettings.leverage", "The leverage is at least 1.")]);
    }

    public decimal Leverage(BotVenueSettings settings)
    {
        using JsonDocument document = JsonDocument.Parse(settings.Json);

        return document.RootElement.TryGetProperty("leverage", out JsonElement leverage)
            ? decimal.Parse(leverage.GetString()!, CultureInfo.InvariantCulture)
            : 1m;
    }

    private static BotVenueSettings Write(decimal leverage)
    {
        return new BotVenueSettings(version: 1, JsonSerializer.Serialize(new { leverage = leverage.ToString(CultureInfo.InvariantCulture) }));
    }
}