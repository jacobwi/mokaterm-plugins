using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Moka.Certificates.Crypto;

namespace Moka.Certificates.Tests.Crypto;

public sealed class CertificateGeneratorTests
{
	[Theory]
	[InlineData(KeyAlgorithm.Rsa, RsaKeySize.Bits2048, EllipticCurve.P256, "RSA 2048")]
	[InlineData(KeyAlgorithm.Rsa, RsaKeySize.Bits3072, EllipticCurve.P256, "RSA 3072")]
	[InlineData(KeyAlgorithm.Ecdsa, RsaKeySize.Bits2048, EllipticCurve.P256, "ECDSA P-256")]
	[InlineData(KeyAlgorithm.Ecdsa, RsaKeySize.Bits2048, EllipticCurve.P384, "ECDSA P-384")]
	[InlineData(KeyAlgorithm.Ecdsa, RsaKeySize.Bits2048, EllipticCurve.P521, "ECDSA P-521")]
	public void CreateKey_MakesWhatWasAskedFor(KeyAlgorithm algorithm, RsaKeySize size, EllipticCurve curve, string expected)
	{
		using AsymmetricAlgorithm key = CertificateGenerator.CreateKey(new KeySpec(algorithm, size, curve));

		Assert.Equal(expected, CertificateReader.DescribeKey(new PublicKey(key)).Description);
	}

	[Fact]
	public void CreateSelfSigned_ReadsBackWithEverythingItWasGiven()
	{
		using RSA key = TestMaterial.RsaKey();
		CertificateSpec spec = TestMaterial.Spec("svc.example", UsagePresets.TlsServer | UsagePresets.TlsClient, 200, "svc.example", "*.svc.example", "10.0.0.1", "2001:db8::1");

		using X509Certificate2 certificate = CertificateGenerator.CreateSelfSigned(key, spec, TestMaterial.Now);
		CertificateInfo info = CertificateReader.Describe(certificate, TestMaterial.Now, 30);
		Dictionary<string, ExtensionInfo> extensions = info.Extensions.ToDictionary(extension => extension.Name);

		Assert.True(info.IsSelfSigned);
		Assert.Equal(TestMaterial.Now.AddMinutes(-5), info.NotBefore);
		Assert.Equal(TestMaterial.Now.AddMinutes(-5).AddDays(200), info.NotAfter);
		Assert.Equal(["Not a certificate authority (CA:FALSE)"], extensions["Basic constraints"].Lines);
		Assert.Equal(["Digital signature", "Key encipherment"], extensions["Key usage"].Lines);
		Assert.True(extensions["Key usage"].Critical);
		Assert.Equal(["TLS server (1.3.6.1.5.5.7.3.1)", "TLS client (1.3.6.1.5.5.7.3.2)"], extensions["Extended key usage"].Lines);
		Assert.Equal(["svc.example", "*.svc.example", "10.0.0.1", "2001:db8::1"], info.AlternativeNames.Select(name => name.Value));
		Assert.Equal(extensions["Subject key identifier"].Lines[0], extensions["Authority key identifier"].Lines[0]["Key ID ".Length..]);
	}

	[Fact]
	public void SerialNumbers_AreRandomPositiveAndSixteenBytes()
	{
		HashSet<string> seen = [];
		for (int index = 0; index < 64; index++)
		{
			byte[] serial = CertificateGenerator.NewSerialNumber();
			Assert.Equal(16, serial.Length);
			Assert.True(serial[0] is > 0 and < 0x80);
			Assert.True(seen.Add(Convert.ToHexString(serial)));
		}
	}

	[Fact]
	public void CreateSelfSigned_Authority_IsACaWithItsPathLength()
	{
		using ECDsa key = TestMaterial.EcKey();
		CertificateSpec spec = TestMaterial.Spec("Root", UsagePresets.CertificateAuthority) with { PathLength = 1 };

		using X509Certificate2 root = CertificateGenerator.CreateSelfSigned(key, spec, TestMaterial.Now);
		X509BasicConstraintsExtension constraints = root.Extensions.OfType<X509BasicConstraintsExtension>().Single();
		X509KeyUsageExtension usage = root.Extensions.OfType<X509KeyUsageExtension>().Single();

		Assert.True(constraints.CertificateAuthority);
		Assert.True(constraints.HasPathLengthConstraint);
		Assert.Equal(1, constraints.PathLengthConstraint);
		Assert.Equal(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, usage.KeyUsages);
		Assert.Empty(root.Extensions.OfType<X509EnhancedKeyUsageExtension>());
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void CreateSigned_ChainBuildsAgainstItsOwnRoot(bool rsa)
	{
		using TestChain chain = TestMaterial.Chain(rsa);
		using X509Chain builder = new();
		builder.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
		builder.ChainPolicy.CustomTrustStore.Add(chain.Root);
		builder.ChainPolicy.ExtraStore.Add(chain.Intermediate);
		builder.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
		builder.ChainPolicy.VerificationTime = TestMaterial.Now.UtcDateTime;
		builder.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));

		bool built = builder.Build(chain.Leaf);

		Assert.True(built, string.Join(", ", builder.ChainStatus.Select(status => status.Status)));
		Assert.Equal(3, builder.ChainElements.Count);
	}

	[Fact]
	public void CreateSigned_CutsTheValidityToTheCas()
	{
		using ECDsa caKey = TestMaterial.EcKey();
		using X509Certificate2 ca = CertificateGenerator.CreateSelfSigned(caKey, TestMaterial.Spec("Short CA", UsagePresets.CertificateAuthority, 30), TestMaterial.Now);
		using ECDsa key = TestMaterial.EcKey();

		using IssuedCertificate issued = CertificateGenerator.CreateSigned(ca, caKey, new PublicKey(key), TestMaterial.Spec("leaf.example", days: 365, names: "leaf.example"), TestMaterial.Now);

		Assert.Equal(ca.NotAfter, issued.Certificate.NotAfter);
		Assert.Contains("cut to end with the CA", Assert.Single(issued.Notes), StringComparison.Ordinal);
	}

	[Fact]
	public void CreateSigned_RefusesWhatACaCannotSign()
	{
		using ECDsa caKey = TestMaterial.EcKey();
		using ECDsa other = TestMaterial.EcKey();
		using ECDsa key = TestMaterial.EcKey();
		using X509Certificate2 notCa = TestMaterial.SelfSigned(caKey);
		using X509Certificate2 ca = CertificateGenerator.CreateSelfSigned(caKey, TestMaterial.Spec("CA", UsagePresets.CertificateAuthority) with { PathLength = 0 }, TestMaterial.Now);
		CertificateSpec leaf = TestMaterial.Spec("leaf.example");

		Assert.Contains("not a CA", Assert.Throws<CertificateException>(() => CertificateGenerator.CreateSigned(notCa, caKey, new PublicKey(key), leaf, TestMaterial.Now)).Message, StringComparison.Ordinal);
		Assert.Contains("does not belong", Assert.Throws<CertificateException>(() => CertificateGenerator.CreateSigned(ca, other, new PublicKey(key), leaf, TestMaterial.Now)).Message, StringComparison.Ordinal);
		Assert.Contains("allows no CAs below it", Assert.Throws<CertificateException>(() => CertificateGenerator.CreateSigned(ca, caKey, new PublicKey(key), TestMaterial.Spec("Sub", UsagePresets.CertificateAuthority), TestMaterial.Now)).Message, StringComparison.Ordinal);
		Assert.Contains("expired", Assert.Throws<CertificateException>(() => CertificateGenerator.CreateSigned(ca, caKey, new PublicKey(key), leaf, TestMaterial.Now.AddYears(2))).Message, StringComparison.Ordinal);
	}

	[Fact]
	public void SignRequest_CertifiesTheRequestsKeyAndNames_AndNeverItsExtensions()
	{
		using TestChain chain = TestMaterial.Chain();
		using ECDsa key = TestMaterial.EcKey();
		CertificateRequest wanting = new(new X500DistinguishedName("CN=greedy.example"), key, HashAlgorithmName.SHA256);
		wanting.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
		SubjectAlternativeNameBuilder names = new();
		names.AddDnsName("greedy.example");
		wanting.CertificateExtensions.Add(names.Build());
		byte[] der = wanting.CreateSigningRequest();
		using MaterialSet set = TestMaterial.ReadFile(der);

		using IssuedCertificate issued = CertificateGenerator.SignRequest(chain.Intermediate, chain.IntermediateKey, Assert.Single(set.Requests), new CertificateSpec { ValidityDays = 30 }, TestMaterial.Now);
		CertificateInfo info = CertificateReader.Describe(issued.Certificate, TestMaterial.Now, 30);

		Assert.Equal("CN=greedy.example", info.Subject);
		Assert.Equal(["greedy.example"], info.AlternativeNames.Select(name => name.Value));
		Assert.False(info.IsCertificateAuthority);
		Assert.True(KeyIdentity.Matches(new PublicKey(key), issued.Certificate.PublicKey));
		Assert.Equal(chain.Intermediate.SubjectName.RawData, issued.Certificate.IssuerName.RawData);
		Assert.Contains("names came from the request", Assert.Single(issued.Notes), StringComparison.Ordinal);
	}

	[Fact]
	public void SignRequest_TamperedRequest_IsRefused()
	{
		using TestChain chain = TestMaterial.Chain();
		using ECDsa key = TestMaterial.EcKey();
		byte[] der = CertificateReaderTests.Tamper(CertificateGenerator.CreateSigningRequest(key, TestMaterial.Spec("api.example")), "api.example", "evil.exampl");
		using MaterialSet set = TestMaterial.ReadFile(der);

		CertificateException refused = Assert.Throws<CertificateException>(() =>
			CertificateGenerator.SignRequest(chain.Intermediate, chain.IntermediateKey, Assert.Single(set.Requests), new CertificateSpec(), TestMaterial.Now));

		Assert.Contains("signature does not verify", refused.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void CreateSigningRequest_AsksForTheEmailUsageWithKeyAgreementForEc()
	{
		using ECDsa key = TestMaterial.EcKey();

		byte[] der = CertificateGenerator.CreateSigningRequest(key, TestMaterial.Spec("Mail User", UsagePresets.Email, 365, "user@example.com"));
		using MaterialSet set = TestMaterial.ReadFile(der);
		RequestInfo info = CertificateReader.Describe(Assert.Single(set.Requests));

		Assert.True(info.SignatureValid);
		Assert.Equal(["Digital signature", "Key agreement"], info.Extensions.Single(extension => extension.Name == "Key usage").Lines);
		Assert.Equal(GeneralNameKind.Email, Assert.Single(info.AlternativeNames).Kind);
	}

	[Fact]
	public void Spec_Problems_AreInWords()
	{
		CertificateSpec empty = new() { Subject = new SubjectSpec() };
		CertificateSpec badCountry = TestMaterial.Spec("x") with { Subject = new SubjectSpec { CommonName = "x", Country = "USA" } };
		CertificateSpec tooLong = TestMaterial.Spec("x", days: 0);

		Assert.Contains("Give it a common name or at least one name.", empty.Problems(forRequest: false));
		Assert.Contains("The country is two letters, such as US or DE.", badCountry.Problems(forRequest: true));
		Assert.Single(tooLong.Problems(forRequest: false));
		Assert.Empty(tooLong.Problems(forRequest: true));
		Assert.Throws<CertificateException>(() => badCountry.Subject.ToName());
	}

	[Fact]
	public void SubjectSpec_ToName_PutsTheGeneralPartsFirst()
	{
		X500DistinguishedName name = new SubjectSpec { CommonName = "a.example", Organization = "Org", Country = "de", Email = "x@example.com" }.ToName();

		AsnReader reader = new AsnReader(name.RawData, AsnEncodingRules.DER).ReadSequence();
		AsnReader first = reader.ReadSetOf().ReadSequence();

		Assert.Equal("2.5.4.6", first.ReadObjectIdentifier());
		Assert.Equal("E=x@example.com, CN=a.example, O=Org, C=DE", name.Name);
	}
}
