using System.Globalization;
using System.Text.Json;

using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Venues.Bybit.Trading;

/// <summary>
///     A USDT-perpetual bot's settings (ADR 0061, owner: "плечо - настраиваемый параметр"), version 1: <c>{"leverage":"3"}</c>. The
///     leverage is an exact decimal string from 1 to 100 with at most two decimals, 1 by default; Bybit refuses one above the
///     contract's own maximum when the gateway sets it. Unknown fields are refused.
/// </summary>
internal sealed class BybitLinearBotSettingsSchema : IVenueBotSettings, IVenueLeverage
{
    public const int Version = 1;
    public const decimal MaxLeverage = 100m;

    private const string LeverageField = "leverage";

    public VenueId Venue => BybitMarket.Linear.Venue;

    public BotVenueSettings Defaults { get; } = Write(leverage: 1m);

    public BotVenueSettings Read(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object)
        {
            throw new ApplicationValidationException([Error("VenueSettings", "Bybit Futures bot settings are an object.")]);
        }

        var errors = new List<InputError>();
        var leverage = 1m;

        foreach (JsonProperty property in settings.EnumerateObject())
        {
            if (property.Name != LeverageField)
            {
                errors.Add(Error("VenueSettings." + property.Name, "Bybit Futures bots have no such setting."));
            }
            else if (Number(property.Value) is { } value && IsValid(value))
            {
                leverage = value;
            }
            else
            {
                errors.Add(Error("VenueSettings." + LeverageField, "The leverage is from 1 to 100 with at most two decimals."));
            }
        }

        return errors.Count == 0 ? Write(leverage) : throw new ApplicationValidationException(errors);
    }

    public decimal Leverage(BotVenueSettings settings)
    {
        return Parse(settings);
    }

    public static bool IsValid(decimal leverage)
    {
        return leverage is >= 1m and <= MaxLeverage && decimal.Round(leverage, decimals: 2) == leverage;
    }

    /// <summary>
    ///     A stored bot's leverage; settings of another version or shape are not guessed. A bot without settings trades at 1×.
    /// </summary>
    public static decimal Parse(BotVenueSettings stored)
    {
        if (stored.Version == 0)
        {
            return 1m;
        }

        if (stored.Version != Version)
        {
            throw new InvalidOperationException("The bot's Bybit Futures settings have an unknown version.");
        }

        using JsonDocument document = JsonDocument.Parse(stored.Json);

        return Number(document.RootElement.GetProperty(LeverageField)) is { } leverage && IsValid(leverage)
            ? leverage
            : throw new InvalidOperationException("The stored leverage is unreadable.");
    }

    public static BotVenueSettings Write(decimal leverage)
    {
        string json = JsonSerializer.Serialize(
            new Dictionary<string, object>(StringComparer.Ordinal) { [LeverageField] = leverage.ToString(CultureInfo.InvariantCulture) });

        return new BotVenueSettings(Version, json);
    }

    private static decimal? Number(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal text) => text,
            JsonValueKind.Number when value.TryGetDecimal(out decimal number) => number,
            _ => null
        };
    }

    private static InputError Error(string field, string message)
    {
        return new InputError("invalid_value", field, message);
    }
}