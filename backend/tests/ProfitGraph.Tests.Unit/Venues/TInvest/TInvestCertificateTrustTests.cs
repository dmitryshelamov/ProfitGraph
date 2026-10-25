using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using ProfitGraph.Venues.TInvest.Transport;

namespace ProfitGraph.Tests.Unit.Venues.TInvest;

/// <summary>
///     The pinned Russian Trusted CA files and the validator that trusts them only for T-Invest (Profit Fox Designer
///     ADR 0085). Chains for the validator are invented here; no broker certificate is used.
/// </summary>
public sealed class TInvestCertificateTrustTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(year: 2026, month: 10, day: 26, hour: 0, minute: 0, second: 0, TimeSpan.Zero);

    [Test]
    public void ThePinnedFilesShipWithTheModule_AndChain()
    {
        using TInvestTrustedCertificates certificates = TInvestTrustedCertificates.Load("Certificates/TInvest", Now);

        certificates.Root.Subject.ShouldContain("Russian Trusted Root CA");
        certificates.Intermediate.Subject.ShouldContain("Russian Trusted Sub CA");
    }

    [Test]
    public void AnExpiredSubCaOrAFileWithAnotherFingerprint_FailsToLoad()
    {
        Should.Throw<InvalidOperationException>(() => TInvestTrustedCertificates.Load(
            "Certificates/TInvest",
            new DateTimeOffset(year: 2027, month: 3, day: 7, hour: 0, minute: 0, second: 0, TimeSpan.Zero)));
        string directory = Directory.CreateTempSubdirectory("tinvest-trust").FullName;
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Certificates/TInvest", TInvestTrustedCertificates.RootFileName),
            Path.Combine(directory, TInvestTrustedCertificates.RootFileName));
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Certificates/TInvest", TInvestTrustedCertificates.RootFileName),
            Path.Combine(directory, TInvestTrustedCertificates.IntermediateFileName));

        Should.Throw<InvalidOperationException>(() => TInvestTrustedCertificates.Load(directory, Now)).Message.ShouldContain("fingerprint");
        Should.Throw<InvalidOperationException>(() => TInvestTrustedCertificates.Load(Path.Combine(directory, "missing"), Now)).Message.ShouldContain("missing");
    }

    [Test]
    public void ALeafUnderThePinnedChainIsAccepted_AndAnythingElseIsNot()
    {
        using var rootKey = RSA.Create(keySizeInBits: 2048);
        using X509Certificate2 root = Authority("CN=Invented Root", rootKey, issuer: null);
        using var intermediateKey = RSA.Create(keySizeInBits: 2048);
        using X509Certificate2 intermediate = Authority("CN=Invented Sub", intermediateKey, root);
        using X509Certificate2 leaf = Leaf("CN=mock-tinvest.invalid", intermediate);
        using var strangerKey = RSA.Create(keySizeInBits: 2048);
        using X509Certificate2 stranger = Authority("CN=Stranger Root", strangerKey, issuer: null);
        using X509Certificate2 foreign = Leaf("CN=mock-tinvest.invalid", stranger);
        var validator = new TInvestCertificateValidator(root, intermediate);

        validator.Validate(leaf, presented: null, SslPolicyErrors.RemoteCertificateChainErrors, X509RevocationMode.NoCheck).ShouldBeTrue();
        validator.Validate(foreign, presented: null, SslPolicyErrors.RemoteCertificateChainErrors, X509RevocationMode.NoCheck).ShouldBeFalse();
        validator.Validate(leaf, presented: null, SslPolicyErrors.RemoteCertificateNameMismatch, X509RevocationMode.NoCheck).ShouldBeFalse();
        validator.Validate(certificate: null, presented: null, SslPolicyErrors.RemoteCertificateNotAvailable, X509RevocationMode.NoCheck).ShouldBeFalse();
        validator.Validate(foreign, presented: null, SslPolicyErrors.None, X509RevocationMode.NoCheck).ShouldBeTrue();
    }

    private static X509Certificate2 Authority(string subject, RSA key, X509Certificate2? issuer)
    {
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        if (issuer is null)
        {
            return request.CreateSelfSigned(Now.AddDays(-1), Now.AddYears(1));
        }

        using X509Certificate2 signed = request.Create(issuer, Now.AddDays(-1), Now.AddMonths(6), Serial());

        return signed.CopyWithPrivateKey(key);
    }

    private static X509Certificate2 Leaf(string subject, X509Certificate2 issuer)
    {
        using var key = RSA.Create(keySizeInBits: 2048);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false));

        return request.Create(issuer, Now.AddDays(-1), Now.AddMonths(3), Serial());
    }

    private static byte[] Serial()
    {
        return RandomNumberGenerator.GetBytes(count: 16);
    }
}