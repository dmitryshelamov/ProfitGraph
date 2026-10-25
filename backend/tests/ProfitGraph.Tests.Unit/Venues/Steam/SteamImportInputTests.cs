using ProfitGraph.Venues.Steam.Accounts;

namespace ProfitGraph.Tests.Unit.Venues.Steam;

/// <summary>
///     Authenticator files with invented values in the common layouts (Profit Fox Designer import command).
/// </summary>
public sealed class SteamImportInputTests
{
    private const string Authenticator = """
                                         "shared_secret": "c2hhcmVkLW5vdC1yZWFs", "identity_secret": "aWRlbnRpdHktbm90LXJlYWw=",
                                         "device_id": "android:00000000-0000-0000-0000-000000000000"
                                         """;

    [Test]
    public void ReadsAFileWithItsTokenInTheSession()
    {
        (SteamImportInput? input, string? problem) = SteamImportInput.Parse(
            $$"""{ "owner": "owner", "label": "Main", "authenticator": { {{Authenticator}}, "Session": { "SteamID": 76561190000000001, "RefreshToken": "token-not-real" } } }""");

        problem.ShouldBeNull();
        input!.SteamId.ShouldBe("76561190000000001");
        input.RefreshToken.ShouldBe("token-not-real");
        input.Fields().Keys.ShouldBe(SteamCredentialFields.All, ignoreOrder: true);
        input.ToString().ShouldNotContain("token-not-real");
        input.ToString().ShouldNotContain("c2hhcmVk");
    }

    [Test]
    public void ReadsAFileWithItsTokenAtTheRoot()
    {
        (SteamImportInput? input, _) = SteamImportInput.Parse(
            $$"""{ "owner": "owner", "label": "Main", "authenticator": { {{Authenticator}}, "steamid": "76561190000000002", "refresh_token": "token-not-real" } }""");

        input!.SteamId.ShouldBe("76561190000000002");
    }

    [TestCase(arg1: """{ "owner": "owner", "label": "Main" }""", "The authenticator file is missing.")]
    [TestCase(arg1: """{ "label": "Main", "authenticator": {} }""", "Give an object with owner, label and authenticator.")]
    [TestCase(arg1: """{ "owner": "o", "label": "Main", "authenticator": { "steamid": "123" } }""", "The authenticator file has no 17-digit Steam ID.")]
    [TestCase(arg1: """{ "owner": "o", "label": "Main", "authenticator": { "steamid": "76561190000000001" } }""", "The authenticator file has no refresh token.")]
    [TestCase(
        arg1: """{ "owner": "o", "label": "Main", "authenticator": { "steamid": "76561190000000001", "RefreshToken": "t" } }""",
        "The authenticator file needs shared_secret, identity_secret and device_id.")]
    [TestCase("not json", "The input is not valid JSON.")]
    public void NamesWhatIsMissingWithoutRepeatingValues(string json, string expected)
    {
        (SteamImportInput? input, string? problem) = SteamImportInput.Parse(json);

        input.ShouldBeNull();
        problem.ShouldBe(expected);
    }
}