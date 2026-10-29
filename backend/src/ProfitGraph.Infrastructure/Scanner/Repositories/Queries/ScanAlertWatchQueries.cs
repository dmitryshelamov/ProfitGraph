namespace ProfitGraph.Infrastructure.Scanner.Repositories.Queries;

internal static class ScanAlertWatchQueries
{
    private const string Columns =
        "watch.preset_id AS PresetId, watch.owner_id AS OwnerId, watch.enabled_at AS EnabledAt, watch.checked_at AS CheckedAt, watch.pairs::text AS Pairs";

    internal const string GetForUpdate = $"SELECT {Columns} FROM scan_alert_watches AS watch WHERE watch.preset_id = @PresetId FOR UPDATE";

    internal const string ListForUpdate = $"""
                                           SELECT {Columns}
                                           FROM scan_alert_watches AS watch
                                           JOIN scan_presets AS preset ON preset.id = watch.preset_id
                                           WHERE preset.venue = @Venue
                                           ORDER BY watch.preset_id
                                           FOR UPDATE OF watch
                                           """;

    internal const string ListPresets = "SELECT preset_id FROM scan_alert_watches WHERE owner_id = @OwnerId";

    internal const string Add = """
                                INSERT INTO scan_alert_watches (preset_id, owner_id, enabled_at, checked_at, pairs)
                                VALUES (@PresetId, @OwnerId, @EnabledAt, @CheckedAt, CAST(@Pairs AS jsonb))
                                """;

    internal const string Update = "UPDATE scan_alert_watches SET checked_at = @CheckedAt, pairs = CAST(@Pairs AS jsonb) WHERE preset_id = @PresetId";

    internal const string Delete = "DELETE FROM scan_alert_watches WHERE preset_id = @PresetId";
}