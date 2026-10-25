using System.Security.Cryptography;
using System.Text;

namespace ProfitGraph.Venues.TInvest.Transport;

/// <summary>
///     An Invest API token. It is sent only as call metadata and prints as redacted; limits are kept per its fingerprint,
///     never per its text.
/// </summary>
internal sealed class TInvestToken
{
    internal string Value { get; }

    internal string Fingerprint { get; }

    internal TInvestToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A T-Invest token cannot be empty.", nameof(value));
        }

        Value = value;
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    public override string ToString()
    {
        return "[redacted]";
    }
}