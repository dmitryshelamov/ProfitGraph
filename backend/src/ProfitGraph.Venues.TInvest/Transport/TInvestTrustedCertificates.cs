using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ProfitGraph.Venues.TInvest.Transport;

/// <summary>
///     The Russian Trusted Root CA and Sub CA that sign the Invest API's certificate, read from files whose DER
///     fingerprints are pinned here (Profit Fox Designer ADR 0085). Replacing a file cannot add trust; a rotation is a
///     release with new pins. The Sub CA expires on 2027-03-06.
/// </summary>
internal sealed class TInvestTrustedCertificates(X509Certificate2 root, X509Certificate2 intermediate) : IDisposable
{
    internal const string RootFileName = "russian_trusted_root_ca.cer";
    internal const string IntermediateFileName = "russian_trusted_sub_ca.cer";
    internal const string RootFingerprint = "D26D2D0231B7C39F92CC738512BA54103519E4405D68B5BD703E9788CA8ECF31";
    internal const string IntermediateFingerprint = "BBBDE2103E790B999EC62BD03CF625A5A2E7C316E10AFE6A490EEDEAD8B3FD9B";

    internal X509Certificate2 Root { get; } = root;

    internal X509Certificate2 Intermediate { get; } = intermediate;

    public void Dispose()
    {
        Root.Dispose();
        Intermediate.Dispose();
    }

    /// <summary>
    ///     Reads both files from <paramref name="directory" /> (relative to the application directory), checks their pins,
    ///     that both are certificate authorities valid at <paramref name="at" /> and that the Sub CA chains to the root.
    /// </summary>
    internal static TInvestTrustedCertificates Load(string directory, DateTimeOffset at)
    {
        string path = Path.GetFullPath(directory, AppContext.BaseDirectory);
        X509Certificate2 root = Read(Path.Combine(path, RootFileName), RootFingerprint);

        try
        {
            X509Certificate2 intermediate = Read(Path.Combine(path, IntermediateFileName), IntermediateFingerprint);

            try
            {
                RequireAuthority(root, at);
                RequireAuthority(intermediate, at);
                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(root);
                chain.ChainPolicy.DisableCertificateDownloads = true;
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.VerificationTime = at.UtcDateTime;

                if (!chain.Build(intermediate) || chain.ChainElements.Count != 2 ||
                    !chain.ChainElements[^1].Certificate.RawData.AsSpan().SequenceEqual(root.RawData))
                {
                    throw new InvalidOperationException("The pinned T-Invest Sub CA does not chain to its pinned root.");
                }

                return new TInvestTrustedCertificates(root, intermediate);
            }
            catch
            {
                intermediate.Dispose();

                throw;
            }
        }
        catch
        {
            root.Dispose();

            throw;
        }
    }

    private static X509Certificate2 Read(string path, string fingerprint)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"The pinned T-Invest certificate {Path.GetFileName(path)} is missing.");
        }

        X509Certificate2 certificate = X509CertificateLoader.LoadCertificateFromFile(path);

        if (!Convert.ToHexString(SHA256.HashData(certificate.RawData)).Equals(fingerprint, StringComparison.Ordinal))
        {
            certificate.Dispose();

            throw new InvalidOperationException($"The T-Invest certificate {Path.GetFileName(path)} does not match its pinned fingerprint.");
        }

        return certificate;
    }

    private static void RequireAuthority(X509Certificate2 certificate, DateTimeOffset at)
    {
        bool authority = certificate.Extensions.OfType<X509BasicConstraintsExtension>().Any(extension => extension.CertificateAuthority);

        if (!authority || at.UtcDateTime < certificate.NotBefore.ToUniversalTime() || at.UtcDateTime > certificate.NotAfter.ToUniversalTime())
        {
            throw new InvalidOperationException("A pinned T-Invest certificate is not a valid certificate authority now.");
        }
    }
}