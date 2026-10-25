namespace ProfitGraph.Domain.Bots;

/// <summary>
///     A bot's settings owned by its venue module (ADR 0003, ADR 0022 C9): the module's version of their shape and a JSON
///     object it defines. The bot stores them without reading their fields; a venue without settings has
///     <see cref="None" />.
/// </summary>
public sealed record BotVenueSettings
{
    public const int MaxLength = 4096;

    public static readonly BotVenueSettings None = new BotVenueSettings(version: 0, "{}");

    public int Version { get; }

    public string Json { get; }

    public BotVenueSettings(int version, string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        if (version < 0 || json.Length is < 2 or > MaxLength || json[0] != '{' || json[^1] != '}')
        {
            throw new BotRuleException(BotRuleException.InvalidSettings, "Venue settings are a versioned JSON object of bounded size.");
        }

        Version = version;
        Json = json;
    }
}