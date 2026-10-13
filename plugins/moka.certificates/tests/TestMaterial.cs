using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Moka.Certificates.Crypto;

namespace Moka.Certificates.Tests;

/// <summary>Keys and certificates the tests make for themselves, at a fixed time, through the plugin's own generator.</summary>
internal static class TestMaterial
{
	public static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

	public static CertificateSpec Spec(string commonName, UsagePresets usages = UsagePresets.TlsServer, int days = 365, params string[] names) => new()
	{
		Subject = new SubjectSpec { CommonName = commonName, Organization = "Moka Test", Country = "US" },
		Names = [.. names.Select(name => SubjectNames.Parse(name).Names[0])],
		Usages = usages,
		ValidityDays = days,
	};

	public static ECDsa EcKey() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

	public static RSA RsaKey() => RSA.Create(2048);

	public static X509Certificate2 SelfSigned(AsymmetricAlgorithm key, string commonName = "test.example", params string[] names) =>
		CertificateGenerator.CreateSelfSigned(key, Spec(commonName, UsagePresets.TlsServer, 365, names.Length > 0 ? names : [commonName]), Now);

	/// <summary>A root CA, an intermediate it signed, and a leaf the intermediate signed, each with its key.</summary>
	public static TestChain Chain(bool rsa = false)
	{
		AsymmetricAlgorithm rootKey = rsa ? RsaKey() : EcKey();
		X509Certificate2 root = CertificateGenerator.CreateSelfSigned(rootKey, Spec("Moka Test Root", UsagePresets.CertificateAuthority, 3650), Now);
		AsymmetricAlgorithm intermediateKey = rsa ? RsaKey() : EcKey();
		IssuedCertificate intermediate = CertificateGenerator.CreateSigned(root, rootKey, new PublicKey(intermediateKey), Spec("Moka Test Intermediate", UsagePresets.CertificateAuthority, 1825), Now);
		AsymmetricAlgorithm leafKey = rsa ? RsaKey() : EcKey();
		IssuedCertificate leaf = CertificateGenerator.CreateSigned(
			intermediate.Certificate,
			intermediateKey,
			new PublicKey(leafKey),
			Spec("www.example.com", UsagePresets.TlsServer, 90, "www.example.com", "example.com", "192.0.2.10"),
			Now);
		return new TestChain(root, rootKey, intermediate.Certificate, intermediateKey, leaf.Certificate, leafKey);
	}

	public static string Pem(params X509Certificate2[] certificates) => CertificateConverter.CertificatesPem(certificates);

	public static MaterialSet Read(string text, string? password = null) => CertificateReader.Read(CryptoInput.FromText(text), password);

	public static MaterialSet ReadFile(byte[] bytes, string? password = null) => CertificateReader.Read(CryptoInput.FromFile("test.bin", bytes), password);
}
