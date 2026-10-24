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
            key,
            secret,
            Created);

        credential.ShouldSatisfyAllConditions(
            () => credential.Id.ShouldBe(id),
            () => credential.OwnerId.ShouldBe(owner),
            () => credential.Venue.ShouldBe(new VenueId("bybit")),
            () => credential.Label.Value.ShouldBe("main key"),
            () => credential.ApiKeyLast4.Value.ShouldBe("real"),
            () => credential.ApiKey.ShouldBeSameAs(key),
            () => credential.ApiSecret.ShouldBeSameAs(secret),
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
            Secret(0x01),
            Secret(0x02),
            Created));
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
            Secret(0x01),
            Secret(0x02),
            Created);
    }

    private static EncryptedSecret Secret(byte fill)
    {
        return new EncryptedSecret("test-v1", new byte[EncryptedSecret.NonceLength], [fill, fill], new byte[EncryptedSecret.TagLength]);
    }
}