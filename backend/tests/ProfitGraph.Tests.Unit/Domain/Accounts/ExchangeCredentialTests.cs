using ProfitGraph.Domain.Accounts;
using ProfitGraph.Domain.MarketData;

namespace ProfitGraph.Tests.Unit.Domain.Accounts;

public sealed class ExchangeCredentialTests
{
    private static readonly DateTimeOffset Created = new DateTimeOffset(year: 2026, month: 10, day: 25, hour: 10, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void CreateKeepsMetadataAndEncryptedValues()
    {
        var id = ExchangeCredentialId.New();
        var owner = UserId.New();
        EncryptedSecret key = Secret(0x01);
        EncryptedSecret secret = Secret(0x02);

        var credential = ExchangeCredential.Create(
            id,
            owner,
            new VenueId("bybit"),
            new ExchangeCredentialLabel("  main key  "),
            ApiKeyLast4.Of("test-api-key-not-real"),
            Secrets(key, secret),
            Created);

        credential.ShouldSatisfyAllConditions(
            () => credential.Id.ShouldBe(id),
            () => credential.OwnerId.ShouldBe(owner),
            () => credential.Venue.ShouldBe(new VenueId("bybit")),
            () => credential.Label.Value.ShouldBe("main key"),
            () => credential.ApiKeyLast4.Value.ShouldBe("real"),
            () => credential.Secrets.Keys.ShouldBe(["apiKey", "apiSecret"]),
            () => credential.Secrets["apiKey"].ShouldBeSameAs(key),
            () => credential.Secrets["apiSecret"].ShouldBeSameAs(secret),
            () => credential.CreatedAt.ShouldBe(Created));
    }

    [Test]
    public void OnlyTheOwnerOwnsACredential()
    {
        var owner = UserId.New();
        ExchangeCredential credential = Credential(owner);

        credential.IsOwnedBy(owner).ShouldBeTrue();
        credential.IsOwnedBy(UserId.New()).ShouldBeFalse();
    }

    [Test]
    public void CredentialNeedsAVenue()
    {
        Should.Throw<ArgumentException>(() => ExchangeCredential.Create(
            ExchangeCredentialId.New(),
            UserId.New(),
            venue: default,
            new ExchangeCredentialLabel("main"),
            new ApiKeyLast4("abcd"),
            Secrets(Secret(0x01), Secret(0x02)),
            Created));
    }

    [Test]
    public void KeepsAnyNamedFieldsTheVenueDeclares()
    {
        var credential = ExchangeCredential.Create(
            ExchangeCredentialId.New(),
            UserId.New(),
            new VenueId("other"),
            new ExchangeCredentialLabel("account"),
            new ApiKeyLast4("1234"),
            new Dictionary<string, EncryptedSecret> { ["sharedSecret"] = Secret(0x03), ["identity2"] = Secret(0x04) },
            Created);

        credential.Secrets.Keys.ShouldBe(["identity2", "sharedSecret"]);
    }

    [TestCase("")]
    [TestCase("api key")]
    [TestCase("api-key")]
    [TestCase("abcdefghijklmnopqrstuvwxyz0123456")]
    public void FieldNamesAreShortAsciiWords(string field)
    {
        Should.Throw<ArgumentException>(() => ExchangeCredential.Create(
            ExchangeCredentialId.New(),
            UserId.New(),
            new VenueId("other"),
            new ExchangeCredentialLabel("account"),
            new ApiKeyLast4("1234"),
            new Dictionary<string, EncryptedSecret> { [field] = Secret(0x03) },
            Created));
    }

    [Test]
    public void ACredentialHasAtLeastOneValue()
    {
        Should.Throw<ArgumentException>(() => ExchangeCredential.Create(
            ExchangeCredentialId.New(),
            UserId.New(),
            new VenueId("other"),
            new ExchangeCredentialLabel("account"),
            new ApiKeyLast4("1234"),
            new Dictionary<string, EncryptedSecret>(),
            Created));
    }

    [Test]
    public void ACredentialMayRecordTheFingerprintOfItsVenueAccount()
    {
        ExchangeCredential plain = WithFingerprint(fingerprint: null);
        ExchangeCredential selected = WithFingerprint("0123abcd");

        plain.AccountFingerprint.ShouldBeNull();
        selected.AccountFingerprint.ShouldBe("0123abcd");
    }

    [TestCase("")]
    [TestCase("with space")]
    public void AnAccountFingerprintIsAShortWord(string fingerprint)
    {
        Should.Throw<ArgumentException>(() => WithFingerprint(fingerprint));
        Should.Throw<ArgumentException>(() => WithFingerprint(new string(c: 'a', count: 129)));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void LabelCannotBeEmpty(string? label)
    {
        Should.Throw<AccountRuleException>(() => new ExchangeCredentialLabel(label)).Code.ShouldBe(AccountRuleException.InvalidCredentialLabel);
    }

    [Test]
    public void LabelIsLimitedAfterTrimming()
    {
        new ExchangeCredentialLabel($" {new string(c: 'a', ExchangeCredentialLabel.MaxLength)} ").Value.Length.ShouldBe(ExchangeCredentialLabel.MaxLength);
        Should.Throw<AccountRuleException>(() => new ExchangeCredentialLabel(new string(c: 'a', ExchangeCredentialLabel.MaxLength + 1)));
    }

    [TestCase("abc")]
    [TestCase("")]
    public void ApiKeyShorterThanFourCharactersIsInvalid(string apiKey)
    {
        Should.Throw<AccountRuleException>(() => ApiKeyLast4.Of(apiKey)).Code.ShouldBe(AccountRuleException.InvalidApiKey);
    }

    [TestCase("ab c")]
    [TestCase("abcde")]
    [TestCase(null)]
    public void VisibleKeyPartHasExactlyFourCharactersWithoutSpaces(string? value)
    {
        Should.Throw<AccountRuleException>(() => new ApiKeyLast4(value)).Code.ShouldBe(AccountRuleException.InvalidApiKey);
    }

    [Test]
    public void EncryptedSecretRequiresGcmSizes()
    {
        Should.Throw<ArgumentException>(() => new EncryptedSecret("k1", new byte[11], [1], new byte[16]));
        Should.Throw<ArgumentException>(() => new EncryptedSecret("k1", new byte[12], [], new byte[16]));
        Should.Throw<ArgumentException>(() => new EncryptedSecret("k1", new byte[12], [1], new byte[15]));
        Should.Throw<ArgumentException>(() => new EncryptedSecret(" ", new byte[12], [1], new byte[16]));
        Should.Throw<ArgumentException>(() => new EncryptedSecret(new string(c: 'k', EncryptedSecret.MaxKeyIdLength + 1), new byte[12], [1], new byte[16]));
    }

    [Test]
    public void EncryptedSecretPrintsOnlyItsKeyIdentifier()
    {
        Secret(0x41).ToString().ShouldBe("EncryptedSecret { KeyId = test-v1 }");
    }

    [Test]
    public void ExchangeCredentialIdCannotBeEmpty()
    {
        Should.Throw<ArgumentException>(() => new ExchangeCredentialId(Guid.Empty));
    }

    private static ExchangeCredential Credential(UserId owner)
    {
        return ExchangeCredential.Create(
            ExchangeCredentialId.New(),
            owner,
            new VenueId("bybit"),
            new ExchangeCredentialLabel("main"),
            new ApiKeyLast4("abcd"),
            Secrets(Secret(0x01), Secret(0x02)),
            Created);
    }

    private static Dictionary<string, EncryptedSecret> Secrets(EncryptedSecret key, EncryptedSecret secret)
    {
        return new Dictionary<string, EncryptedSecret> { ["apiSecret"] = secret, ["apiKey"] = key };
    }

    private static EncryptedSecret Secret(byte fill)
    {
        return new EncryptedSecret("test-v1", new byte[EncryptedSecret.NonceLength], [fill, fill], new byte[EncryptedSecret.TagLength]);
    }

    private static ExchangeCredential WithFingerprint(string? fingerprint)
    {
        return ExchangeCredential.Create(
            ExchangeCredentialId.New(),
            UserId.New(),
            new VenueId("other"),
            new ExchangeCredentialLabel("account"),
            new ApiKeyLast4("1234"),
            new Dictionary<string, EncryptedSecret> { ["token"] = Secret(0x05) },
            Created,
            fingerprint);
    }
}