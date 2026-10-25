using System.Globalization;
using System.Text.Json;

using ProfitGraph.Application.Bots.Ports;
using ProfitGraph.Application.Common.Exceptions;
using ProfitGraph.Domain.Bots;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Venues.Steam.Trading;

/// <summary>
///     Checks a Steam bot's settings and writes them in version 1:
///     <c>{"settlementHoldDays":7,"marketBuySlippage":"0","marketBuyDeadlineSeconds":120}</c>. Missing fields take their
///     defaults and unknown fields are refused. The slippage is an exact decimal string from 0 to 0.1, the hold whole days
///     up to 30 and the deadline whole seconds from 10 to 3600.
/// </summary>
internal sealed class SteamBotSettingsSchema : IVenueBotSettings
{
    private const string HoldDays = "settlementHoldDays";
    private const string Slippage = "marketBuySlippage";
    private const string DeadlineSeconds = "marketBuyDeadlineSeconds";

    public VenueId Venue => SteamVenue.VenueId;

    public BotVenueSettings Defaults { get; } = Write(SteamBotSettings.Defaults);

    public BotVenueSettings Read(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object)
        {
            throw new ApplicationValidationException([new InputError("invalid_value", "VenueSettings", "Steam bot settings are an object.")]);
        }

        var errors = new List<InputError>();
        var value = SteamBotSettings.Defaults;

        foreach (JsonProperty property in settings.EnumerateObject())
        {
            switch (property.Name)
            {
                case HoldDays when Integer(property.Value) is { } days and >= 0 and <= 30:
                    value = value with { SettlementHoldDays = days };

                    break;
                case Slippage when Fraction(property.Value) is { } slippage and >= 0m and <= 0.1m:
                    value = value with { MarketBuySlippage = slippage };

                    break;
                case DeadlineSeconds when Integer(property.Value) is { } seconds and >= 10 and <= 3600:
                    value = value with { MarketBuyDeadlineSeconds = seconds };

                    break;
                case HoldDays or Slippage or DeadlineSeconds:
                    errors.Add(Error(property.Name, "The value is outside the allowed range."));

                    break;
                default:
                    errors.Add(Error(property.Name, "Steam bots have no such setting."));

                    break;
            }
        }

        return errors.Count == 0 ? Write(value) : throw new ApplicationValidationException(errors);
    }

    /// <summary>
    ///     A stored bot's settings; settings of another version or shape are not guessed.
    /// </summary>
    public static SteamBotSettings Parse(BotVenueSettings stored)
    {
        if (stored.Version != SteamBotSettings.Version)
        {
            throw new InvalidOperationException("The bot's Steam settings have an unknown version.");
        }

        using JsonDocument document = JsonDocument.Parse(stored.Json);
        JsonElement root = document.RootElement;

        return new SteamBotSettings(
            Integer(root.GetProperty(HoldDays)) ?? throw new InvalidOperationException("The stored hold is unreadable."),
            Fraction(root.GetProperty(Slippage)) ?? throw new InvalidOperationException("The stored slippage is unreadable."),
            Integer(root.GetProperty(DeadlineSeconds)) ?? throw new InvalidOperationException("The stored deadline is unreadable."));
    }

    private static BotVenueSettings Write(SteamBotSettings settings)
    {
        string json = JsonSerializer.Serialize(
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [HoldDays] = settings.SettlementHoldDays,
                [Slippage] = settings.MarketBuySlippage.ToString(CultureInfo.InvariantCulture),
                [DeadlineSeconds] = settings.MarketBuyDeadlineSeconds
            });

        return new BotVenueSettings(SteamBotSettings.Version, json);
    }

    private static int? Integer(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : null;
    }

    private static decimal? Fraction(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.String &&
            decimal.TryParse(value.GetString(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal number)
                ? number
                : null;
    }

    private static InputError Error(string field, string message)
    {
        return new InputError("invalid_value", "VenueSettings." + field, message);
    }
}