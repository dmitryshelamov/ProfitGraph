namespace ProfitGraph.Domain.Accounts;

/// <summary>
///     One secret value encrypted with authenticated encryption: the master key that encrypted it, a fresh nonce, the
///     ciphertext and the authentication tag. The Domain only carries it; encryption lives in Infrastructure.
/// </summary>
public sealed record EncryptedSecret
{
    public const int NonceLength = 12;
    public const int TagLength = 16;
    public const int MaxKeyIdLength = 64;

    public string KeyId { get; }
    public byte[] Nonce { get; }
    public byte[] Ciphertext { get; }
    public byte[] Tag { get; }

    public EncryptedSecret(string keyId, byte[] nonce, byte[] ciphertext, byte[] tag)
    {
        ArgumentNullException.ThrowIfNull(nonce);
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentNullException.ThrowIfNull(tag);

        if (string.IsNullOrWhiteSpace(keyId) || keyId.Length > MaxKeyIdLength || nonce.Length != NonceLength || ciphertext.Length == 0 || tag.Length != TagLength)
        {
            throw new ArgumentException("Invalid encrypted secret.");
        }

        KeyId = keyId;
        Nonce = nonce;
        Ciphertext = ciphertext;
        Tag = tag;
    }

    public override string ToString()
    {
        return $"EncryptedSecret {{ KeyId = {KeyId} }}";
    }
}