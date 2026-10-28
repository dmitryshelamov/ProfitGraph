namespace ProfitGraph.Domain.Accounts;

/// <summary>
///     One secret value encrypted with authenticated encryption: the master key that encrypted it, a fresh nonce, the
///     ciphertext and the authentication tag. The Domain only carries it; encryption lives in Infrastructure. It keeps its own
///     read-only copies of the bytes, and two secrets are equal when their key and bytes are.
/// </summary>
public sealed record EncryptedSecret
{
    public const int NonceLength = 12;
    public const int TagLength = 16;
    public const int MaxKeyIdLength = 64;

    public string KeyId { get; }
    public ReadOnlyMemory<byte> Nonce { get; }
    public ReadOnlyMemory<byte> Ciphertext { get; }
    public ReadOnlyMemory<byte> Tag { get; }

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
        Nonce = nonce.ToArray();
        Ciphertext = ciphertext.ToArray();
        Tag = tag.ToArray();
    }

    public bool Equals(EncryptedSecret? other)
    {
        return other is not null && KeyId == other.KeyId && Nonce.Span.SequenceEqual(other.Nonce.Span) && Ciphertext.Span.SequenceEqual(other.Ciphertext.Span) &&
            Tag.Span.SequenceEqual(other.Tag.Span);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(KeyId, StringComparer.Ordinal);
        hash.AddBytes(Ciphertext.Span);

        return hash.ToHashCode();
    }

    public override string ToString()
    {
        return $"EncryptedSecret {{ KeyId = {KeyId} }}";
    }
}