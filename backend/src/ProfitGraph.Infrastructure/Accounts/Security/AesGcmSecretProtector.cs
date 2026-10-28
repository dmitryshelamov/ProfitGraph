using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ProfitGraph.Application.Accounts;
using ProfitGraph.Domain.Accounts;

namespace ProfitGraph.Infrastructure.Accounts.Security;

/// <summary>
///     AES-256-GCM with a fresh 12-byte nonce and a 16-byte tag per value (ADR 0015). The associated data
///     <c>{credentialId:D}|{ownerId:D}|{field}</c> binds a value to its row and field. Every configured key decrypts the
///     values stored under its identifier; only the active key encrypts. Key material never reaches a log.
/// </summary>
internal sealed class AesGcmSecretProtector : ISecretProtector
{
    private const int MasterKeyLength = 32;

    private readonly Dictionary<string, byte[]> _keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    private readonly string? _activeKeyId;

    public bool IsAvailable => _activeKeyId is not null && _keys.ContainsKey(_activeKeyId);

    public AesGcmSecretProtector(IOptions<SecretsOptions> options, ILogger<AesGcmSecretProtector> logger)
    {
        SecretsOptions secrets = options.Value;
        _activeKeyId = string.IsNullOrWhiteSpace(secrets.ActiveKeyId) ? null : secrets.ActiveKeyId;

        foreach ((string keyId, string encoded) in secrets.MasterKeys)
        {
            byte[]? key = Decode(encoded);

            if (key is not null && keyId.Length <= EncryptedSecret.MaxKeyIdLength)
            {
                _keys[keyId] = key;
            }
            else
            {
                logger.LogWarning("Master key {KeyId} is not a base64-encoded 32-byte key and is ignored.", keyId);
            }
        }

        if (!IsAvailable)
        {
            logger.LogWarning("No active credential master key is configured; credential endpoints answer 503.");
        }
    }

    public EncryptedSecret Encrypt(ExchangeCredentialId credentialId, UserId ownerId, string field, string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);

        if (!IsAvailable)
        {
            throw new CredentialsUnavailableException();
        }

        byte[] nonce = RandomNumberGenerator.GetBytes(EncryptedSecret.NonceLength);
        byte[] input = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[input.Length];
        var tag = new byte[EncryptedSecret.TagLength];

        try
        {
            using var aes = new AesGcm(_keys[_activeKeyId!], EncryptedSecret.TagLength);
            aes.Encrypt(nonce, input, ciphertext, tag, AssociatedData(credentialId, ownerId, field));

            return new EncryptedSecret(_activeKeyId!, nonce, ciphertext, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    /// <summary>
    ///     HMAC-SHA256 of the value under a key derived from each master key with HKDF for the purpose, as lowercase hex.
    /// </summary>
    public IReadOnlyList<string> Fingerprints(string purpose, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(purpose);
        ArgumentException.ThrowIfNullOrEmpty(value);

        if (!IsAvailable)
        {
            throw new CredentialsUnavailableException();
        }

        byte[] input = Encoding.UTF8.GetBytes(value);
        byte[] info = Encoding.UTF8.GetBytes($"profitgraph-fingerprint/{purpose}");

        try
        {
            return
            [
                .. _keys
                    .OrderBy(pair => pair.Key == _activeKeyId ? 0 : 1)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => Convert.ToHexStringLower(HMACSHA256.HashData(HKDF.DeriveKey(HashAlgorithmName.SHA256, pair.Value, MasterKeyLength, [], info), input)))
            ];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    public string Decrypt(ExchangeCredentialId credentialId, UserId ownerId, string field, EncryptedSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        if (!_keys.TryGetValue(secret.KeyId, out byte[]? key))
        {
            throw new CredentialsUnavailableException();
        }

        var plaintext = new byte[secret.Ciphertext.Length];

        try
        {
            using var aes = new AesGcm(key, EncryptedSecret.TagLength);
            aes.Decrypt(secret.Nonce.Span, secret.Ciphertext.Span, secret.Tag.Span, plaintext, AssociatedData(credentialId, ownerId, field));

            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[]? Decode(string? encoded)
    {
        try
        {
            byte[] key = Convert.FromBase64String(encoded ?? string.Empty);

            return key.Length == MasterKeyLength ? key : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static byte[] AssociatedData(ExchangeCredentialId credentialId, UserId ownerId, string field)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);

        return Encoding.UTF8.GetBytes($"{credentialId.Value:D}|{ownerId.Value:D}|{field}");
    }
}