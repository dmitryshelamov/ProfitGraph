using ProfitGraph.Application.Accounts.Ports;
using ProfitGraph.Application.MarketData;

namespace ProfitGraph.Application.Accounts;

/// <summary>
///     Which named fields a venue's web form takes (ADR 0022, C6; ADR 0023, TC5). Messages name fields only, never values.
/// </summary>
internal static class CredentialFieldRules
{
    public const int MaxValueLength = 4096;

    /// <summary>
    ///     The fields a complete credential of the venue has, or null when the venue takes no credentials by fields.
    /// </summary>
    public static IReadOnlyList<string>? Declared(
        string? venue,
        VenueRegistry venues,
        VenuePorts<IVenueCredentialSchema> schemas,
        VenuePorts<IVenueAccountDirectory> directories)
    {
        return venues.Find(venue) is { } descriptor && directories.Find(descriptor.Id) is not null ? schemas.Find(descriptor.Id)?.Fields : null;
    }

    public static bool Matches(IReadOnlyDictionary<string, string>? fields, IEnumerable<string> expected)
    {
        return fields is not null &&
            expected.Order(StringComparer.Ordinal).SequenceEqual(fields.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal) &&
            fields.Values.All(value => value is { Length: > 0 and <= MaxValueLength } && value.Trim() == value);
    }
}