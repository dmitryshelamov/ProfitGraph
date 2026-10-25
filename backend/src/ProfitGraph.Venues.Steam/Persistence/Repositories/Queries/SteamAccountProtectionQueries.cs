namespace ProfitGraph.Venues.Steam.Persistence.Repositories.Queries;

internal static class SteamAccountProtectionQueries
{
    internal const string Get = "SELECT reason AS Reason, opened_at AS OpenedAt FROM steam_account_protection WHERE credential_id = @credentialId";

    internal const string Open = """
                                 INSERT INTO steam_account_protection (credential_id, reason, opened_at) VALUES (@credentialId, @reason, @openedAt)
                                 ON CONFLICT (credential_id) DO NOTHING
                                 """;

    internal const string Clear = "DELETE FROM steam_account_protection WHERE credential_id = @credentialId";
}