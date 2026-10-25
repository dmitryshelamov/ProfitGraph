using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ProfitGraph.Venues.TInvest.Transport;

/// <summary>
///     Accepts the Invest API's certificate when the platform trusts it, or when it chains to the pinned root through the
///     pinned Sub CA (Profit Fox Designer ADR 0085). The extra trust exists only in T-Invest's own handler; a missing
///     certificate or a host name mismatch always fails, and the handler's revocation mode is kept.
/// </summary>
internal sealed class TInvestCertificateValidator(X509Certificate2 root, X509Certificate2 intermediate)
{
    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";

    public SocketsHttpHandler CreateHandler()
    {
        var handler = new SocketsHttpHandler();
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, chain, errors) =>
            Validate(certificate, chain, errors, handler.SslOptions.CertificateRevocationCheckMode);

        return handler;
    }

    internal bool Validate(X509Certificate? certificate, X509Chain? presented, SslPolicyErrors errors, X509RevocationMode revocation)
    {
        if (certificate is null || (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0)
        {
            return false;
        }

        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        try
        {
            using X509Certificate2 leaf = X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(root);
            chain.ChainPolicy.ExtraStore.Add(intermediate);
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid(ServerAuthentication));
            chain.ChainPolicy.DisableCertificateDownloads = true;
            chain.ChainPolicy.RevocationMode = revocation;
            chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;

            if (presented is not null)
            {
                foreach (X509ChainElement element in presented.ChainElements)
                {
                    chain.ChainPolicy.ExtraStore.Add(element.Certificate);
                }
            }

            return chain.Build(leaf) &&
                chain.ChainStatus.All(status => status.Status == X509ChainStatusFlags.NoError) &&
                chain.ChainElements.Count > 1 &&
                chain.ChainElements[^1].Certificate.RawData.AsSpan().SequenceEqual(root.RawData);
        }
        catch (Exception error) when (error is CryptographicException or ArgumentException or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}