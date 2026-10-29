namespace ProfitGraph.Infrastructure.Scanner.Repositories.Queries;

internal static class ScanPresetQueries
{
    private const string Columns =
        "id AS Id, owner_id AS OwnerId, venue AS Venue, name AS Name, definition::text AS DefinitionJson, created_at AS CreatedAt, updated_at AS UpdatedAt";

    internal const string GetForUpdate = $"SELECT {Columns} FROM scan_presets WHERE id = @Id AND owner_id = @OwnerId FOR UPDATE";

    internal const string List = $"SELECT {Columns} FROM scan_presets WHERE owner_id = @OwnerId AND venue = @Venue";

    internal const string NameTaken = """
                                      SELECT EXISTS (
                                          SELECT 1 FROM scan_presets
                                          WHERE owner_id = @OwnerId AND venue = @Venue AND lower(name) = lower(@Name) AND (@Except::uuid IS NULL OR id <> @Except))
                                      """;

    internal const string Add = """
                                INSERT INTO scan_presets (id, owner_id, venue, name, definition, created_at, updated_at)
                                VALUES (@Id, @OwnerId, @Venue, @Name, @DefinitionJson::jsonb, @CreatedAt, @UpdatedAt)
                                """;

    internal const string Update = """
                                   UPDATE scan_presets SET name = @Name, definition = @DefinitionJson::jsonb, updated_at = @UpdatedAt
                                   WHERE id = @Id AND owner_id = @OwnerId
                                   """;

    internal const string Delete = "DELETE FROM scan_presets WHERE id = @Id AND owner_id = @OwnerId";
}