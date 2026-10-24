using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Accounts;
using ProfitGraph.Domain.Accounts;
using ProfitGraph.Infrastructure.Accounts.Security;

namespace ProfitGraph.Tests.Unit.Infrastructure.Accounts;

public sealed class AesGcmSecretProtectorTests
{
    private const string Plaintext = "test-api-secret-not-real";

    private static readonly string FirstKey = Convert.ToBase64String(Enumerable.Repeat((byte)0x11, count: 32).ToArray());
    private static readonly string SecondKey = Convert.ToBase64String(Enumerable.Repeat((byte)0x22, count: 32).ToArray());

    private readonly ExchangeCredentialId _credential = ExchangeCredentialId.New();
    private readonly UserId _owner = UserId.New();

    [Test]
    public void RoundTripUsesAFreshNonceForEqualPlaintext()
    {
        AesGcmSecretProtector protector = Create("test-v1");

        EncryptedSecret first = protector.Encrypt(_credential, _owner, ExchangeCredentialFields.ApiKey, Plaintext);
        EncryptedSecret second = protector.Encrypt(_credential, _owner, ExchangeCredentialFields.ApiKey, Plaintext);

        first.ShouldSatisfyAllConditions(
            () => first.KeyId.ShouldBe("test-v1"),
            () => first.Nonce.Length.ShouldBe(EncryptedSecret.NonceLength),
            () => first.Tag.Length.ShouldBe(EncryptedSecret.TagLength),
            () => first.Nonce.ShouldNotBe(second.Nonce),
            () => first.Ciphertext.ShouldNotBe(second.Ciphertext),
            () => Encoding.UTF8.GetString(first.Ciphertext).ShouldNotContain(Plaintext),
            () => protector.Decrypt(_credential, _owner, ExchangeCredentialFields.ApiKey, first).ShouldBe(Plaintext),
            () => protector.Decrypt(_credential, _owner, ExchangeCredentialFields.ApiKey, second).ShouldBe(Plaintext));
    }

    [Test]
    public void TamperedCiphertextOrAnotherRowOrFieldCannotBeDecrypted()
    {
        AesGcmSecretProtector protector = Create("test-v1");
        EncryptedSecret encrypted = protector.Encrypt(_credential, _owner, ExchangeCredentialFields.ApiSecret, Plaintext);
        var tamperedBytes = (byte[])encrypted.Ciphertext.Clone();
        tamperedBytes[0] ^= 0x40;
        var tampered = new EncryptedSecret(encrypted.KeyId, encrypted.Nonce, tamperedBytes, encrypted.Tag);
        var tamperedTagBytes = (byte[])encrypted.Tag.Clone();
        tamperedTagBytes[0] ^= 0x01;
        var tamperedTag = new EncryptedSecret(encrypted.KeyId, encrypted.Nonce, encrypted.Ciphertext, tamperedTagBytes);

        Should.Throw<CryptographicException>(() => protector.Decrypt(_credential, _owner, ExchangeCredentialFields.ApiSecret, tampered));
        Should.Throw<CryptographicException>(() => protector.Decrypt(_credential, _owner, ExchangeCredentialFields.ApiSecret, tamperedTag));
        Should.Throw<CryptographicException>(() => protector.Decrypt(ExchangeCredentialId.New(), _owner, ExchangeCredentialFields.ApiSecret, encrypted));
        Should.Throw<CryptographicException>(() => protector.Decrypt(_credential, UserId.New(), ExchangeCredentialFields.ApiSecret, encrypted));
        Should.Throw<CryptographicException>(() => protector.Decrypt(_credential, _owner, ExchangeCredentialFields.ApiKey, encrypted));
    }

    [Test]
    public void OlderConfiguredKeyStillDecryptsAfterRotation()
    {
        EncryptedSecret old = Create("test-v1").Encrypt(_credential, _owner, ExchangeCredentialFields.ApiKey, Plaintext);
        AesGcmSecretProtector rotated = Create("test-v2");

        rotated.Decrypt(_credential, _owner, ExchangeCredentialFields.ApiKey, old).ShouldBe(Plaintext);
        rotated.Encrypt(_credential, _owner, ExchangeCredentialFields.ApiKey, Plaintext).KeyId.ShouldBe("test-v2");
    }

    [Test]
    public void ValueOfARemovedKeyIsUnavailable()
    {
        EncryptedSecret old = Create("test-v1").Encrypt(_credential, _owner, ExchangeCredentialFields.ApiKey, Plaintext);
        AesGcmSecretProtector withoutOldKey = Create("test-v2", new Dictionary<string, string> { ["test-v2"] = SecondKey });

        Should.Throw<CredentialsUnavailableException>(() => withoutOldKey.Decrypt(_credential, _owner, ExchangeCredentialFields.ApiKey, old));
    }

    [Test]
    public void MissingActiveKeyMakesStorageUnavailable()
    {
        AesGcmSecretProtector protector = Create(activeKeyId: null);

        protector.IsAvailable.ShouldBeFalse();
        Should.Throw<CredentialsUnavailableException>(() => protector.Encrypt(_credential, _owner, ExchangeCredentialFields.ApiKey, Plaintext));
    }

    [TestCase("not base64")]
    [TestCase("AAAA")]
    public void InvalidMasterKeyIsIgnored(string encoded)
    {
        AesGcmSecretProtector protector = Create("broken", new Dictionary<string, string> { ["broken"] = encoded });

        protector.IsAvailable.ShouldBeFalse();
    }

    private static AesGcmSecretProtector Create(string? activeKeyId, Dictionary<string, string>? keys = null)
    {
        var options = new SecretsOptions
        {
            ActiveKeyId = activeKeyId,
            MasterKeys = keys ?? new Dictionary<string, string> { ["test-v1"] = FirstKey, ["test-v2"] = SecondKey }
        };

        return new AesGcmSecretProtector(Options.Create(options), NullLogger<AesGcmSecretProtector>.Instance);
    }
}