namespace ProfitGraph.Infrastructure.Accounts.Security;

/// <summary>
///     Section <c>Secrets</c>: base64-encoded 32-byte master keys by stable key identifier and the identifier that
///     encrypts new values. Values come only from user secrets or the environment, never from a committed file.
/// </summary>
internal sealed class SecretsOptions
{
    internal const string SectionName = "Secrets";

    public string? ActiveKeyId { get; init; }
    public Dictionary<string, string> MasterKeys { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}