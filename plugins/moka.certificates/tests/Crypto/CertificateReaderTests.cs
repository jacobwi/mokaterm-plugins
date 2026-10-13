using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Moka.Certificates.Crypto;

namespace Moka.Certificates.Tests.Crypto;

public sealed class CertificateReaderTests
{
	[Fact]
	public void Read_PemCertificate_DescribesIt()
	{
		using ECDsa key = TestMaterial.EcKey();
		using X509Certificate2 certificate = TestMaterial.SelfSigned(key, "host.example", "host.example", "192.0.2.5", "admin@example.com", "https://host.example/");

		using MaterialSet set = TestMaterial.Read(TestMaterial.Pem(certificate));
		CertificateInfo info = CertificateReader.Describe(Assert.Single(set.Certificates), TestMaterial.Now, 30);

		Assert.Equal("host.example", info.Title);
		Assert.Equal("CN=host.example, O=Moka Test, C=US", info.Subject);
		Assert.Equal(info.Subject, info.Issuer);
		Assert.Equal(certificate.SerialNumber, info.SerialNumber);
		Assert.Equal("ECDSA P-256", info.Key.Description);
		Assert.Equal("ECDSA with SHA-256", info.SignatureAlgorithm);
		Assert.True(info.IsSelfIssued);
		Assert.True(info.IsSelfSigned);
		Assert.False(info.IsCertificateAuthority);
		Assert.Equal(
			["DNS:host.example", "IP:192.0.2.5", "email:admin@example.com", "URI:https://host.example/"],
			info.AlternativeNames.Select(name => name.ToString()));
		Assert.Equal(ExpiryState.Valid, info.Expiry);
		Assert.Equal(364, info.DaysLeft);
	}

	[Fact]
	public void Read_Fingerprints_MatchThePlatformsHashes()
	{
		using RSA key = TestMaterial.RsaKey();
		using X509Certificate2 certificate = TestMaterial.SelfSigned(key);

		CertificateInfo info = CertificateReader.Describe(certificate, TestMaterial.Now, 30);

		Assert.Equal(certificate.Thumbprint, info.Sha1.Replace(":", "", StringComparison.Ordinal));
		Assert.Equal(certificate.GetCertHashString(HashAlgorithmName.SHA256), info.Sha256.Replace(":", "", StringComparison.Ordinal));
		Assert.Equal("RSA 2048", info.Key.Description);
		Assert.Equal(Convert.ToBase64String(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo())), info.Key.Pin);
	}

	[Fact]
	public void Read_DerFile_ReadsTheCertificate()
	{
		using ECDsa key = TestMaterial.EcKey();
		using X509Certificate2 certificate = TestMaterial.SelfSigned(key);

		using MaterialSet set = TestMaterial.ReadFile(certificate.RawData);

		CertificateMaterial item = Assert.IsType<CertificateMaterial>(Assert.Single(set.Items));
		Assert.Equal("DER", item.Format);
		Assert.Equal(certificate.RawData, item.Certificate.RawData);
	}

	[Fact]
	public void Read_Base64WithoutArmor_ReadsTheCertificate()
	{
		using ECDsa key = TestMaterial.EcKey();
		using X509Certificate2 certificate = TestMaterial.SelfSigned(key);

		using MaterialSet set = TestMaterial.Read(Convert.ToBase64String(certificate.RawData, Base64FormattingOptions.InsertLineBreaks));

		Assert.Equal(certificate.RawData, Assert.Single(set.Certificates).RawData);
	}

	[Fact]
	public void Read_PemFileWithByteOrderMark_IsReadAsText()
	{
		using ECDsa key = TestMaterial.EcKey();
		using X509Certificate2 certificate = TestMaterial.SelfSigned(key);
		byte[] bytes = [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.ASCII.GetBytes("Bag Attributes\r\n" + TestMaterial.Pem(certificate).Replace("\n", "\r\n", StringComparison.Ordinal))];

		CryptoInput input = CryptoInput.FromFile("cert.pem", bytes);
		using MaterialSet set = CertificateReader.Read(input);

		Assert.NotNull(input.Text);
		Assert.Single(set.Certificates);
	}

	[Fact]
	public void Inspect_BundleInOrder_SaysSo()
	{
		using TestChain chain = TestMaterial.Chain();
		using MaterialSet set = TestMaterial.Read(TestMaterial.Pem(chain.Leaf, chain.Intermediate, chain.Root));

		Inspection inspection = CertificateReader.Inspect(set, TestMaterial.Now, 30);

		ChainCheck check = Assert.IsType<ChainCheck>(inspection.Chain);
		Assert.True(check.InOrder);
		Assert.True(check.EndsAtSelfSigned);
		Assert.All(check.Links, link => Assert.True(link.SignatureValid));
		Assert.Contains("up to a self-signed root", check.Summary, StringComparison.Ordinal);
	}

	[Fact]
	public void Inspect_BundleWithoutRoot_IsInOrder()
	{
		using TestChain chain = TestMaterial.Chain(rsa: true);
		using MaterialSet set = TestMaterial.Read(TestMaterial.Pem(chain.Leaf, chain.Intermediate));

		ChainCheck check = Assert.IsType<ChainCheck>(CertificateReader.Inspect(set, TestMaterial.Now, 30).Chain);

		Assert.True(check.InOrder);
		Assert.False(check.EndsAtSelfSigned);
		Assert.Contains("root is left out", check.Summary, StringComparison.Ordinal);
	}

	[Fact]
	public void Inspect_BundleOutOfOrder_GivesTheRightOrder()
	{
		using TestChain chain = TestMaterial.Chain();
		using MaterialSet set = TestMaterial.Read(TestMaterial.Pem(chain.Intermediate, chain.Leaf, chain.Root));

		ChainCheck check = Assert.IsType<ChainCheck>(CertificateReader.Inspect(set, TestMaterial.Now, 30).Chain);

		Assert.False(check.InOrder);
		Assert.Equal([2, 1, 3], check.CorrectOrder);
		Assert.Contains("did not issue certificate 1", check.Summary, StringComparison.Ordinal);
	}

	[Fact]
	public void Inspect_UnrelatedCertificates_DoNotFormAChain()
	{
		using ECDsa first = TestMaterial.EcKey();
		using ECDsa second = TestMaterial.EcKey();
		using X509Certificate2 one = TestMaterial.SelfSigned(first, "one.example");
		using X509Certificate2 two = TestMaterial.SelfSigned(second, "two.example");
		using MaterialSet set = TestMaterial.Read(TestMaterial.Pem(one, two));

		ChainCheck check = Assert.IsType<ChainCheck>(CertificateReader.Inspect(set, TestMaterial.Now, 30).Chain);

		Assert.False(check.InOrder);
		Assert.Null(check.CorrectOrder);
		Assert.StartsWith("These certificates do not form one chain", check.Summary, StringComparison.Ordinal);
	}

	[Fact]
	public void Inspect_ForgedIssuer_IsCaughtBySignature()
	{
		using TestChain chain = TestMaterial.Chain();
		using ECDsa forger = TestMaterial.EcKey();

		// Same subject as the real intermediate, a different key: the names line up, the signature does not.
		using X509Certificate2 impostor = CertificateGenerator.CreateSelfSigned(forger, TestMaterial.Spec("Moka Test Intermediate", UsagePresets.CertificateAuthority), TestMaterial.Now);
		using MaterialSet set = TestMaterial.Read(TestMaterial.Pem(chain.Leaf, impostor));

		ChainCheck check = Assert.IsType<ChainCheck>(CertificateReader.Inspect(set, TestMaterial.Now, 30).Chain);

		ChainLink link = Assert.Single(check.Links);
		Assert.True(link.NamesMatch);
		Assert.False(link.SignatureValid);
		Assert.False(check.InOrder);
	}

	[Fact]
	public void Inspect_KeyAndCertificate_AreMatched()
	{
		using TestChain chain = TestMaterial.Chain();
		string text = TestMaterial.Pem(chain.Leaf) + CertificateConverter.PrivateKeyPem(chain.LeafKey, PrivateKeyFormat.Pkcs8) + CertificateConverter.PrivateKeyPem(chain.RootKey, PrivateKeyFormat.Traditional);
		using MaterialSet set = TestMaterial.Read(text);

		Inspection inspection = CertificateReader.Inspect(set, TestMaterial.Now, 30);

		Assert.Equal(3, inspection.Items.Count);
		Assert.Contains("item 2", Assert.Single(inspection.Items[0].Matches), StringComparison.Ordinal);
		Assert.Contains("Matches the certificate in item 1", Assert.Single(inspection.Items[1].Matches), StringComparison.Ordinal);
		Assert.Equal("Matches none of the certificates or requests here.", Assert.Single(inspection.Items[2].Matches));
		Assert.Equal("PEM, SEC1", inspection.Items[2].Format);
		Assert.Contains("-----BEGIN PUBLIC KEY-----", inspection.Items[1].PublicKeyPem, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_Pfx_OpensWithItsPassword()
	{
		using TestChain chain = TestMaterial.Chain(rsa: true);
		byte[] pfx = CertificateConverter.ToPfx(chain.Leaf, chain.LeafKey, [chain.Intermediate, chain.Root], new PfxOptions { Password = "s3cret pass" });

		using MaterialSet set = TestMaterial.ReadFile(pfx, "s3cret pass");

		Assert.False(set.NeedsPassword);
		Assert.Equal(3, set.Certificates.Count);
		PrivateKeyMaterial key = Assert.Single(set.PrivateKeys);
		Assert.True(key.WasEncrypted);
		Assert.Equal("PFX", key.Format);
		Assert.Equal(chain.Leaf.RawData, set.CertificateFor(key)!.RawData);
	}

	[Fact]
	public void Read_Pfx_WithoutOrWithTheWrongPassword_IsLocked()
	{
		using TestChain chain = TestMaterial.Chain();
		byte[] pfx = CertificateConverter.ToPfx(chain.Leaf, chain.LeafKey, [], new PfxOptions { Password = "right" });

		using MaterialSet none = TestMaterial.ReadFile(pfx);
		using MaterialSet wrong = TestMaterial.ReadFile(pfx, "wrong");

		Assert.True(none.NeedsPassword);
		Assert.False(none.PasswordRefused);
		Assert.Empty(none.Certificates);
		Assert.True(wrong.NeedsPassword);
		Assert.True(wrong.PasswordRefused);
		Assert.Contains("rounds", Assert.IsType<LockedKeyMaterial>(Assert.Single(wrong.Items)).Protection, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_PfxWithAnEmptyPassword_OpensWithNoneGiven()
	{
		using TestChain chain = TestMaterial.Chain();
		byte[] pfx = CertificateConverter.ToPfx(chain.Leaf, chain.LeafKey, [], new PfxOptions { Password = "" });

		using MaterialSet set = TestMaterial.ReadFile(pfx);

		Assert.False(set.NeedsPassword);
		Assert.Single(set.PrivateKeys);
	}

	[Fact]
	public void Read_PfxMadeByThePlatform_IsRead()
	{
		using TestChain chain = TestMaterial.Chain(rsa: true);
		using X509Certificate2 withKey = chain.Leaf.CopyWithPrivateKey((RSA)chain.LeafKey);
		byte[] pfx = withKey.Export(X509ContentType.Pkcs12, "platform");

		using MaterialSet set = TestMaterial.ReadFile(pfx, "platform");

		Assert.Single(set.Certificates);
		Assert.True(KeyIdentity.Matches(chain.Leaf.PublicKey, Assert.Single(set.PrivateKeys).PublicKey));
	}

	[Fact]
	public void Read_Csr_DescribesItAndChecksItsSignature()
	{
		using RSA key = TestMaterial.RsaKey();
		byte[] der = CertificateGenerator.CreateSigningRequest(key, TestMaterial.Spec("api.example", UsagePresets.TlsServer, 365, "api.example", "api2.example"));

		using MaterialSet set = TestMaterial.Read(CertificateConverter.RequestPem(der));
		RequestInfo info = CertificateReader.Describe(Assert.Single(set.Requests));

		Assert.Equal("api.example", info.Title);
		Assert.True(info.SignatureValid);
		Assert.Equal("RSA with SHA-256", info.SignatureAlgorithm);
		Assert.Equal(["api.example", "api2.example"], info.AlternativeNames.Select(name => name.Value));
		Assert.Contains(info.Extensions, extension => extension.Name == "Extended key usage" && extension.Lines.Contains("TLS server (1.3.6.1.5.5.7.3.1)"));
	}

	[Fact]
	public void Read_TamperedCsr_FailsItsSignatureCheck()
	{
		using ECDsa key = TestMaterial.EcKey();
		byte[] der = CertificateGenerator.CreateSigningRequest(key, TestMaterial.Spec("api.example"));
		byte[] tampered = Tamper(der, "api.example", "apx.example");

		using MaterialSet set = TestMaterial.ReadFile(tampered);
		RequestInfo info = CertificateReader.Describe(Assert.Single(set.Requests));

		Assert.Equal("apx.example", info.Title);
		Assert.False(info.SignatureValid);
	}

	[Theory]
	[InlineData(PrivateKeyFormat.Pkcs8, true, "PEM, PKCS#8")]
	[InlineData(PrivateKeyFormat.Traditional, true, "PEM, PKCS#1")]
	[InlineData(PrivateKeyFormat.Pkcs8, false, "PEM, PKCS#8")]
	[InlineData(PrivateKeyFormat.Traditional, false, "PEM, SEC1")]
	public void Read_PrivateKeys_InEveryPlainForm(PrivateKeyFormat format, bool rsa, string expected)
	{
		using AsymmetricAlgorithm key = rsa ? TestMaterial.RsaKey() : TestMaterial.EcKey();

		using MaterialSet set = TestMaterial.Read(CertificateConverter.PrivateKeyPem(key, format));

		PrivateKeyMaterial read = Assert.Single(set.PrivateKeys);
		Assert.Equal(expected, read.Format);
		Assert.False(read.WasEncrypted);
		Assert.True(KeyIdentity.Matches(new PublicKey(key), read.PublicKey));
	}

	[Fact]
	public void Read_DerPrivateKeys_AreIdentified()
	{
		using RSA rsa = TestMaterial.RsaKey();
		using ECDsa ec = TestMaterial.EcKey();

		using MaterialSet pkcs1 = TestMaterial.ReadFile(rsa.ExportRSAPrivateKey());
		using MaterialSet sec1 = TestMaterial.ReadFile(ec.ExportECPrivateKey());
		using MaterialSet pkcs8 = TestMaterial.ReadFile(ec.ExportPkcs8PrivateKey());

		Assert.Equal("DER, PKCS#1", Assert.Single(pkcs1.PrivateKeys).Format);
		Assert.Equal("DER, SEC1", Assert.Single(sec1.PrivateKeys).Format);
		Assert.Equal("DER, PKCS#8", Assert.Single(pkcs8.PrivateKeys).Format);
	}

	[Fact]
	public void Read_EncryptedKey_OpensOnlyWithItsPassword()
	{
		using ECDsa key = TestMaterial.EcKey();
		string pem = CertificateConverter.EncryptedPrivateKeyPem(key, "correct horse");

		using MaterialSet none = TestMaterial.Read(pem);
		using MaterialSet wrong = TestMaterial.Read(pem, "battery staple");
		using MaterialSet right = TestMaterial.Read(pem, "correct horse");

		LockedKeyMaterial locked = Assert.IsType<LockedKeyMaterial>(Assert.Single(none.Items));
		Assert.False(locked.PasswordRefused);
		Assert.Equal("AES-256-CBC, PBKDF2 with HMAC-SHA256, 100,000 rounds", locked.Protection);
		Assert.True(wrong.PasswordRefused);
		PrivateKeyMaterial opened = Assert.Single(right.PrivateKeys);
		Assert.True(opened.WasEncrypted);
		Assert.True(KeyIdentity.Matches(new PublicKey(key), opened.PublicKey));
	}

	[Fact]
	public void Read_EncryptedKeyAskingForTooMuchWork_IsRefusedBeforeAnyPasswordIsTried()
	{
		using ECDsa key = TestMaterial.EcKey();
		byte[] der = key.ExportEncryptedPkcs8PrivateKey("pw", new PbeParameters(PbeEncryptionAlgorithm.Aes128Cbc, HashAlgorithmName.SHA256, (int)CertificateReader.MaxKdfRounds + 1));

		using MaterialSet set = TestMaterial.Read(CertificateConverter.ToPem("ENCRYPTED PRIVATE KEY", der), "pw");

		Assert.Empty(set.Items);
		Assert.Contains("rounds of password hashing", Assert.Single(set.Notes).Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_PublicKeys_SpkiAndPkcs1()
	{
		using RSA key = TestMaterial.RsaKey();

		using MaterialSet spki = TestMaterial.Read(key.ExportSubjectPublicKeyInfoPem());
		using MaterialSet pkcs1 = TestMaterial.Read(key.ExportRSAPublicKeyPem());

		Assert.True(KeyIdentity.Matches(new PublicKey(key), Assert.Single(spki.PublicKeys).Key));
		Assert.True(KeyIdentity.Matches(new PublicKey(key), Assert.Single(pkcs1.PublicKeys).Key));
		Assert.Equal("PEM, PKCS#1", pkcs1.PublicKeys[0].Format);
	}

	[Fact]
	public void Read_Pkcs7Bundle_ReadsItsCertificates()
	{
		using TestChain chain = TestMaterial.Chain();
		X509Certificate2Collection collection = [chain.Leaf, chain.Intermediate];
		byte[] p7b = collection.Export(X509ContentType.Pkcs7)!;

		using MaterialSet set = TestMaterial.ReadFile(p7b);

		Assert.Equal(2, set.Certificates.Count);
		Assert.All(set.Items, item => Assert.Equal("PKCS#7", item.Format));
	}

	[Fact]
	public void Read_Ed25519_IsDescribedButNotUsed()
	{
		byte[] spki = Ed25519PublicKey();
		using MaterialSet publicSet = TestMaterial.Read(CertificateConverter.ToPem("PUBLIC KEY", spki));
		using MaterialSet privateSet = TestMaterial.Read(CertificateConverter.ToPem("PRIVATE KEY", Ed25519PrivateKey()));

		KeyInfo info = CertificateReader.DescribeKey(Assert.Single(publicSet.PublicKeys).Key);
		Assert.Equal("Ed25519", info.Algorithm);
		Assert.False(info.Usable);
		Assert.Contains("not supported in this version", info.Limitation, StringComparison.Ordinal);
		Assert.Empty(privateSet.Items);
		Assert.Contains("Ed25519 is not supported in this version", Assert.Single(privateSet.Notes).Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("-----BEGIN OPENSSH PRIVATE KEY-----\nAAAA\n-----END OPENSSH PRIVATE KEY-----\n", "OpenSSH private keys are not read here")]
	[InlineData("-----BEGIN X509 CRL-----\nAAAA\n-----END X509 CRL-----\n", "revocation list")]
	[InlineData("-----BEGIN RSA PRIVATE KEY-----\nProc-Type: 4,ENCRYPTED\nDEK-Info: AES-128-CBC,00112233445566778899AABBCCDDEEFF\n\nAAAA\n-----END RSA PRIVATE KEY-----\n", "OpenSSL's old encryption")]
	[InlineData("-----BEGIN CERTIFICATE-----\n@@@@\n-----END CERTIFICATE-----\n", "damaged")]
	[InlineData("-----BEGIN CERTIFICATE-----\nMIIB\n", "has no END line")]
	[InlineData("hello there", "Nothing here looks like a certificate or a key")]
	public void Read_WhatItCannotRead_SaysWhy(string text, string expected)
	{
		using MaterialSet set = TestMaterial.Read(text);

		Assert.Empty(set.Items);
		Assert.Contains(expected, Assert.Single(set.Notes).Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Read_GarbageFile_IsNotMistakenForAnything()
	{
		using MaterialSet set = TestMaterial.ReadFile([0x30, 0x03, 0x02, 0x01, 0x05, 0xFF]);

		Assert.Empty(set.Items);
		Assert.Single(set.Notes);
	}

	[Theory]
	[InlineData(-10, ExpiryState.NotYetValid)]
	[InlineData(0, ExpiryState.Valid)]
	[InlineData(340, ExpiryState.Expiring)]
	[InlineData(400, ExpiryState.Expired)]
	public void Describe_Expiry_FollowsTheClockAndTheSetting(int daysLater, ExpiryState expected)
	{
		using ECDsa key = TestMaterial.EcKey();
		using X509Certificate2 certificate = TestMaterial.SelfSigned(key);

		CertificateInfo info = CertificateReader.Describe(certificate, TestMaterial.Now.AddDays(daysLater), 30);

		Assert.Equal(expected, info.Expiry);
	}

	[Fact]
	public void Describe_Extensions_InWords()
	{
		using TestChain chain = TestMaterial.Chain(rsa: true);
		using ECDsa key = TestMaterial.EcKey();
		CertificateRequest request = new(new X500DistinguishedName("CN=ext.example"), key, HashAlgorithmName.SHA256);
		request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
		request.CertificateExtensions.Add(new X509AuthorityInformationAccessExtension(["http://ocsp.example/"], ["http://ca.example/ca.cer"]));
		request.CertificateExtensions.Add(CertificateRevocationListBuilder.BuildCrlDistributionPointExtension(["http://crl.example/ca.crl"]));
		request.CertificateExtensions.Add(new X509Extension("2.5.29.32", Policies(), critical: false));
		request.CertificateExtensions.Add(new X509Extension("1.3.6.1.5.5.7.1.24", MustStaple(), critical: false));
		request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(chain.Intermediate, includeKeyIdentifier: true, includeIssuerAndSerial: false));
		using X509Certificate2 certificate = request.CreateSelfSigned(TestMaterial.Now.AddDays(-1), TestMaterial.Now.AddDays(30));

		CertificateInfo info = CertificateReader.Describe(certificate, TestMaterial.Now, 30);
		Dictionary<string, ExtensionInfo> byName = info.Extensions.ToDictionary(extension => extension.Name);

		Assert.True(info.IsCertificateAuthority);
		Assert.Equal(["A certificate authority (CA:TRUE)", "At most 0 CAs below it"], byName["Basic constraints"].Lines);
		Assert.True(byName["Basic constraints"].Critical);
		Assert.Equal(["OCSP: http://ocsp.example/", "CA issuers: http://ca.example/ca.cer"], byName["Authority information access"].Lines);
		Assert.Equal(["http://crl.example/ca.crl"], byName["CRL distribution points"].Lines);
		Assert.Equal(["Domain validated (DV) (2.23.140.1.2.1)", "  CPS: https://cps.example/"], byName["Certificate policies"].Lines);
		Assert.Equal(["OCSP must-staple (status_request)"], byName["TLS feature"].Lines);
		Assert.StartsWith("Key ID ", Assert.Single(byName["Authority key identifier"].Lines), StringComparison.Ordinal);
	}

	[Fact]
	public void Describe_UnknownExtension_IsNamedByItsNumber()
	{
		using ECDsa key = TestMaterial.EcKey();
		CertificateRequest request = new(new X500DistinguishedName("CN=x"), key, HashAlgorithmName.SHA256);
		request.CertificateExtensions.Add(new X509Extension("1.2.3.4.5", [0x05, 0x00], critical: true));
		using X509Certificate2 certificate = request.CreateSelfSigned(TestMaterial.Now.AddDays(-1), TestMaterial.Now.AddDays(30));

		ExtensionInfo extension = Assert.Single(CertificateReader.Describe(certificate, TestMaterial.Now, 30).Extensions);

		Assert.Equal("1.2.3.4.5", extension.Name);
		Assert.True(extension.Critical);
		Assert.Equal(["2 bytes, not described here"], extension.Lines);
	}

	[Fact]
	public void CertificateReport_TextAndJson_CarryTheDescription()
	{
		using TestChain chain = TestMaterial.Chain();
		CertificateInfo info = CertificateReader.Describe(chain.Leaf, TestMaterial.Now, 30);

		string text = CertificateReport.Text(info);
		using System.Text.Json.JsonDocument json = System.Text.Json.JsonDocument.Parse(CertificateReport.Json(info));

		Assert.Contains("Subject: CN=www.example.com", text, StringComparison.Ordinal);
		Assert.Contains("  DNS:example.com", text, StringComparison.Ordinal);
		Assert.Equal(info.SerialNumber, json.RootElement.GetProperty("serialNumber").GetString());
		Assert.Equal("ECDSA", json.RootElement.GetProperty("key").GetProperty("algorithm").GetString());
		Assert.Equal(3, json.RootElement.GetProperty("alternativeNames").GetArrayLength());
		Assert.DoesNotContain("PRIVATE", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Records_DoNotPrintTheirContents()
	{
		using TestChain chain = TestMaterial.Chain();
		CertificateInfo info = CertificateReader.Describe(chain.Leaf, TestMaterial.Now, 30);
		PemBundle bundle = new() { PrivateKey = "-----BEGIN PRIVATE KEY-----secret" };

		Assert.DoesNotContain("www.example.com", info.ToString(), StringComparison.Ordinal);
		Assert.DoesNotContain("secret", bundle.ToString(), StringComparison.Ordinal);
		Assert.DoesNotContain("pw", new PfxOptions { Password = "pw" }.ToString(), StringComparison.Ordinal);
	}

	internal static byte[] Tamper(byte[] der, string from, string to)
	{
		byte[] copy = [.. der];
		byte[] search = System.Text.Encoding.ASCII.GetBytes(from);
		int at = copy.AsSpan().IndexOf(search);
		Assert.True(at >= 0);
		System.Text.Encoding.ASCII.GetBytes(to).CopyTo(copy, at);
		return copy;
	}

	private static byte[] Ed25519PublicKey()
	{
		AsnWriter writer = new(AsnEncodingRules.DER);
		using (writer.PushSequence())
		{
			using (writer.PushSequence())
			{
				writer.WriteObjectIdentifier("1.3.101.112");
			}

			writer.WriteBitString(RandomNumberGenerator.GetBytes(32));
		}

		return writer.Encode();
	}

	private static byte[] Ed25519PrivateKey()
	{
		AsnWriter inner = new(AsnEncodingRules.DER);
		inner.WriteOctetString(RandomNumberGenerator.GetBytes(32));
		AsnWriter writer = new(AsnEncodingRules.DER);
		using (writer.PushSequence())
		{
			writer.WriteInteger(0);
			using (writer.PushSequence())
			{
				writer.WriteObjectIdentifier("1.3.101.112");
			}

			writer.WriteOctetString(inner.Encode());
		}

		return writer.Encode();
	}

	private static byte[] Policies()
	{
		AsnWriter writer = new(AsnEncodingRules.DER);
		using (writer.PushSequence())
		{
			using (writer.PushSequence())
			{
				writer.WriteObjectIdentifier("2.23.140.1.2.1");
				using (writer.PushSequence())
				{
					using (writer.PushSequence())
					{
						writer.WriteObjectIdentifier("1.3.6.1.5.5.7.2.1");
						writer.WriteCharacterString(UniversalTagNumber.IA5String, "https://cps.example/");
					}
				}
			}
		}

		return writer.Encode();
	}

	private static byte[] MustStaple()
	{
		AsnWriter writer = new(AsnEncodingRules.DER);
		using (writer.PushSequence())
		{
			writer.WriteInteger(5);
		}

		return writer.Encode();
	}
}
