using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Moka.Certificates.Crypto;

namespace Moka.Certificates.Tests.Crypto;

public sealed class CertificateConverterTests
{
	[Fact]
	public void PemToDer_AndBack_KeepEveryBlock()
	{
		using TestChain chain = TestMaterial.Chain(rsa: true);
		byte[] request = CertificateGenerator.CreateSigningRequest(chain.LeafKey, TestMaterial.Spec("r.example"));
		string pem = TestMaterial.Pem(chain.Leaf)
			+ CertificateConverter.RequestPem(request)
			+ CertificateConverter.PrivateKeyPem(chain.LeafKey, PrivateKeyFormat.Traditional)
			+ CertificateConverter.PublicKeyPem(chain.Leaf.PublicKey);

		IReadOnlyList<DerBlock> blocks = CertificateConverter.PemToDer(pem);

		Assert.Equal(["CERTIFICATE", "CERTIFICATE REQUEST", "RSA PRIVATE KEY", "PUBLIC KEY"], blocks.Select(block => block.Label));
		Assert.Equal([".cer", ".csr", ".key.der", ".pub.der"], blocks.Select(block => block.FileExtension));
		Assert.Equal([false, false, true, false], blocks.Select(block => block.IsSecret));
		Assert.Equal(chain.Leaf.RawData, blocks[0].Der);
		Assert.Equal(pem, string.Concat(blocks.Select(block => CertificateConverter.DerToPem(block.Der))));
	}

	[Fact]
	public void DerToPem_RefusesPfxAndUnknown()
	{
		using TestChain chain = TestMaterial.Chain();
		byte[] pfx = CertificateConverter.ToPfx(chain.Leaf, chain.LeafKey, [], new PfxOptions { Password = "p" });

		Assert.Contains("Use PFX to PEM", Assert.Throws<CertificateException>(() => CertificateConverter.DerToPem(pfx)).Message, StringComparison.Ordinal);
		Assert.Throws<CertificateException>(() => CertificateConverter.DerToPem([0x01, 0x02]));
		Assert.Throws<CertificateException>(() => CertificateConverter.PemToDer("no blocks here"));
	}

	[Theory]
	[InlineData(PfxEncryption.Aes256)]
	[InlineData(PfxEncryption.Legacy3Des)]
	public void ToPfx_OpensInThePlatformAndInTheReader(PfxEncryption encryption)
	{
		using TestChain chain = TestMaterial.Chain(rsa: true);
		using MaterialSet source = TestMaterial.Read(TestMaterial.Pem(chain.Root, chain.Leaf, chain.Intermediate) + CertificateConverter.PrivateKeyPem(chain.LeafKey, PrivateKeyFormat.Pkcs8));

		byte[] pfx = CertificateConverter.ToPfx(source, new PfxOptions { Password = "pfx pass", FriendlyName = "Web server", Encryption = encryption });

		X509Certificate2Collection loaded = X509CertificateLoader.LoadPkcs12Collection(pfx, "pfx pass", X509KeyStorageFlags.EphemeralKeySet);
		try
		{
			Assert.Equal(3, loaded.Count);
			Assert.True(loaded.Single(certificate => certificate.HasPrivateKey).RawData.AsSpan().SequenceEqual(chain.Leaf.RawData));
		}
		finally
		{
			foreach (X509Certificate2 certificate in loaded)
			{
				certificate.Dispose();
			}
		}

		Pkcs12Info info = Pkcs12Info.Decode(pfx, out _);
		Assert.True(info.VerifyMac("pfx pass"));
		Pkcs12SafeContents certificates = info.AuthenticatedSafe[0];
		Assert.Equal(Pkcs12ConfidentialityMode.Password, certificates.ConfidentialityMode);
		certificates.Decrypt("pfx pass");
		Pkcs12SafeBag leafBag = certificates.GetBags().First();
		CryptographicAttributeObject friendly = leafBag.Attributes.Cast<CryptographicAttributeObject>().Single(attribute => attribute.Oid.Value == "1.2.840.113549.1.9.20");
		Assert.Equal("Web server", new System.Formats.Asn1.AsnReader(friendly.Values[0].RawData, System.Formats.Asn1.AsnEncodingRules.DER).ReadCharacterString(System.Formats.Asn1.UniversalTagNumber.BMPString));

		using MaterialSet back = TestMaterial.ReadFile(pfx, "pfx pass");
		Assert.Equal([chain.Leaf.RawData, chain.Intermediate.RawData, chain.Root.RawData], back.Certificates.Select(certificate => certificate.RawData));
		Assert.True(KeyIdentity.Matches(chain.Leaf.PublicKey, Assert.Single(back.PrivateKeys).PublicKey));
	}

	[Fact]
	public void ToPfx_RefusesAKeyThatBelongsToNoCertificate()
	{
		using TestChain chain = TestMaterial.Chain();
		using MaterialSet set = TestMaterial.Read(TestMaterial.Pem(chain.Leaf) + CertificateConverter.PrivateKeyPem(chain.RootKey, PrivateKeyFormat.Pkcs8));

		Assert.Contains("belongs to none", Assert.Throws<CertificateException>(() => CertificateConverter.ToPfx(set, new PfxOptions { Password = "p" })).Message, StringComparison.Ordinal);
	}

	[Fact]
	public void PfxToPem_PutsTheLeafFirstAndOrdersTheChain()
	{
		using TestChain chain = TestMaterial.Chain();
		byte[] pfx = CertificateConverter.ToPfx(chain.Leaf, chain.LeafKey, [chain.Root, chain.Intermediate], new PfxOptions { Password = "p" });
		using MaterialSet set = TestMaterial.ReadFile(pfx, "p");

		PemBundle plain = CertificateConverter.PfxToPem(set, PrivateKeyFormat.Traditional, keyPassword: null);
		PemBundle encrypted = CertificateConverter.PfxToPem(set, PrivateKeyFormat.Pkcs8, keyPassword: "new pass");

		Assert.Equal(TestMaterial.Pem(chain.Leaf), plain.Certificate);
		Assert.Equal(TestMaterial.Pem(chain.Intermediate, chain.Root), plain.Chain);
		Assert.StartsWith("-----BEGIN EC PRIVATE KEY-----", plain.PrivateKey, StringComparison.Ordinal);
		Assert.StartsWith("-----BEGIN ENCRYPTED PRIVATE KEY-----", encrypted.PrivateKey, StringComparison.Ordinal);
		using MaterialSet reopened = TestMaterial.Read(encrypted.PrivateKey!, "new pass");
		Assert.True(KeyIdentity.Matches(chain.Leaf.PublicKey, Assert.Single(reopened.PrivateKeys).PublicKey));
	}

	[Fact]
	public void PfxToPem_LockedPfx_SaysSo()
	{
		using TestChain chain = TestMaterial.Chain();
		byte[] pfx = CertificateConverter.ToPfx(chain.Leaf, chain.LeafKey, [], new PfxOptions { Password = "p" });
		using MaterialSet set = TestMaterial.ReadFile(pfx, "nope");

		Assert.Contains("does not open", Assert.Throws<CertificateException>(() => CertificateConverter.PfxToPem(set, PrivateKeyFormat.Pkcs8, null)).Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void PrivateKeyFormats_RoundTrip(bool rsa)
	{
		using AsymmetricAlgorithm key = rsa ? TestMaterial.RsaKey() : TestMaterial.EcKey();
		string pkcs8 = CertificateConverter.PrivateKeyPem(key, PrivateKeyFormat.Pkcs8);
		string traditional = CertificateConverter.PrivateKeyPem(key, PrivateKeyFormat.Traditional);

		using MaterialSet fromPkcs8 = TestMaterial.Read(pkcs8);
		using MaterialSet fromTraditional = TestMaterial.Read(traditional);

		Assert.StartsWith("-----BEGIN PRIVATE KEY-----", pkcs8, StringComparison.Ordinal);
		Assert.StartsWith(rsa ? "-----BEGIN RSA PRIVATE KEY-----" : "-----BEGIN EC PRIVATE KEY-----", traditional, StringComparison.Ordinal);
		Assert.Equal(pkcs8, CertificateConverter.PrivateKeyPem(fromTraditional.PrivateKeys[0].Key, PrivateKeyFormat.Pkcs8));
		Assert.Equal(traditional, CertificateConverter.PrivateKeyPem(fromPkcs8.PrivateKeys[0].Key, PrivateKeyFormat.Traditional));
	}

	[Fact]
	public void EncryptAndDecryptKey_RoundTrip()
	{
		using ECDsa key = TestMaterial.EcKey();
		string encrypted = CertificateConverter.EncryptedPrivateKeyPem(key, "hunter2");

		using MaterialSet opened = TestMaterial.Read(encrypted, "hunter2");
		string decrypted = CertificateConverter.PrivateKeyPem(opened.RequireOnePrivateKey().Key, PrivateKeyFormat.Pkcs8);

		Assert.Equal(CertificateConverter.PrivateKeyPem(key, PrivateKeyFormat.Pkcs8), decrypted);
		Assert.Throws<CertificateException>(() => CertificateConverter.EncryptedPrivateKeyPem(key, ""));
		using MaterialSet wrong = TestMaterial.Read(encrypted, "hunter3");
		Assert.Contains("does not open", Assert.Throws<CertificateException>(wrong.RequireOnePrivateKey).Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ChainAbove_FollowsIssuersAndKeepsLeftovers()
	{
		using TestChain chain = TestMaterial.Chain();
		using ECDsa strayKey = TestMaterial.EcKey();
		using X509Certificate2 stray = TestMaterial.SelfSigned(strayKey, "stray.example");

		IReadOnlyList<X509Certificate2> ordered = CertificateConverter.ChainAbove(chain.Leaf, [stray, chain.Root, chain.Leaf, chain.Intermediate]);

		Assert.Equal([chain.Intermediate.RawData, chain.Root.RawData, stray.RawData], ordered.Select(certificate => certificate.RawData));
	}
}
