using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Moka.DevTools.Hashing;
using Moka.DevTools.Jwt;

namespace Moka.DevTools.Tests.Jwt;

public sealed class JwtVerifierTests
{
	private static readonly byte[] Secret = Encoding.UTF8.GetBytes("a secret that is long enough for HS512, sixty-four bytes or more!!");

	[Fact]
	public void Hmac_VerifiesThePublishedJwtIoToken()
	{
		JwtVerification result = JwtVerifier.VerifyHmac(JwtDecoder.Decode(TestTokens.JwtIo), Encoding.UTF8.GetBytes("your-256-bit-secret"));

		Assert.Equal(JwtVerdict.Verified, result.Verdict);
		Assert.Contains(result.Notes, note => note.Contains("19 bytes", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("HS256")]
	[InlineData("HS384")]
	[InlineData("HS512")]
	public void Hmac_VerifiesAndFails(string algorithm)
	{
		JwtToken token = JwtDecoder.Decode(TestTokens.Hmac(algorithm, Secret));

		Assert.Equal(JwtVerdict.Verified, JwtVerifier.VerifyHmac(token, Secret).Verdict);
		Assert.Empty(JwtVerifier.VerifyHmac(token, Secret).Notes);
		Assert.Equal(JwtVerdict.Failed, JwtVerifier.VerifyHmac(token, Encoding.UTF8.GetBytes("another secret")).Verdict);
		Assert.Equal(JwtVerdict.Failed, JwtVerifier.VerifyHmac(JwtDecoder.Decode(TestTokens.Tampered(TestTokens.Hmac(algorithm, Secret))), Secret).Verdict);
		Assert.Equal(JwtVerdict.NotChecked, JwtVerifier.VerifyHmac(token, []).Verdict);
	}

	[Fact]
	public void Hmac_TakesABase64Secret()
	{
		byte[] key = RandomNumberGenerator.GetBytes(32);
		JwtToken token = JwtDecoder.Decode(TestTokens.Hmac("HS256", key));

		byte[] read = SecretKey.Read(Convert.ToBase64String(key), KeyFormat.Base64);

		Assert.Equal(JwtVerdict.Verified, JwtVerifier.VerifyHmac(token, read).Verdict);
		Assert.Equal(JwtVerdict.Verified, JwtVerifier.VerifyHmac(token, SecretKey.Read(TestTokens.B64(key), KeyFormat.Base64)).Verdict);
		Assert.Equal(JwtVerdict.Verified, JwtVerifier.VerifyHmac(token, SecretKey.Read(Convert.ToHexString(key), KeyFormat.Hex)).Verdict);
	}

	[Theory]
	[InlineData("RS256")]
	[InlineData("RS384")]
	[InlineData("RS512")]
	[InlineData("PS256")]
	[InlineData("PS384")]
	[InlineData("PS512")]
	public void Rsa_VerifiesWithAPublicKeyAndFailsWhenChanged(string algorithm)
	{
		using RSA rsa = RSA.Create(2048);
		string token = TestTokens.Rsa(algorithm, rsa);

		Assert.Equal(JwtVerdict.Verified, Verify(token, rsa.ExportSubjectPublicKeyInfoPem()).Verdict);
		Assert.Equal(JwtVerdict.Failed, Verify(TestTokens.Tampered(token), rsa.ExportSubjectPublicKeyInfoPem()).Verdict);

		using RSA other = RSA.Create(2048);
		Assert.Equal(JwtVerdict.Failed, Verify(token, other.ExportSubjectPublicKeyInfoPem()).Verdict);
	}

	[Fact]
	public void Rsa_ReadsEveryKeyForm()
	{
		using RSA rsa = RSA.Create(2048);
		string token = TestTokens.Rsa("RS256", rsa, keyId: "key-2");
		CertificateRequest request = new("CN=signer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
		using RSA other = RSA.Create(2048);
		string set = $$"""{"keys":[{{TestTokens.RsaJwk(other, "key-1")}},{{TestTokens.RsaJwk(rsa, "key-2")}}]}""";

		Assert.Equal(JwtVerdict.Verified, Verify(token, rsa.ExportRSAPublicKeyPem()).Verdict);
		JwtVerification byCertificate = Verify(token, certificate.ExportCertificatePem());
		Assert.Equal(JwtVerdict.Verified, byCertificate.Verdict);
		Assert.Contains(byCertificate.Notes, note => note.Contains("were not checked", StringComparison.Ordinal));
		Assert.Equal(JwtVerdict.Verified, Verify(token, TestTokens.RsaJwk(rsa)).Verdict);
		Assert.Equal(JwtVerdict.Verified, Verify(token, set).Verdict);
	}

	[Fact]
	public void Rsa_NotesASmallKey()
	{
		using RSA rsa = RSA.Create(1024);

		JwtVerification result = Verify(TestTokens.Rsa("RS256", rsa), rsa.ExportSubjectPublicKeyInfoPem());

		Assert.Equal(JwtVerdict.Verified, result.Verdict);
		Assert.Contains(result.Notes, note => note.Contains("RSA 1024", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("ES256", "P-256")]
	[InlineData("ES384", "P-384")]
	[InlineData("ES512", "P-521")]
	public void Ecdsa_VerifiesWithPemAndJwk(string algorithm, string curve)
	{
		using ECDsa ecdsa = ECDsa.Create(curve switch
		{
			"P-256" => ECCurve.NamedCurves.nistP256,
			"P-384" => ECCurve.NamedCurves.nistP384,
			_ => ECCurve.NamedCurves.nistP521,
		});
		string token = TestTokens.Ecdsa(algorithm, ecdsa);

		Assert.Equal(JwtVerdict.Verified, Verify(token, ecdsa.ExportSubjectPublicKeyInfoPem()).Verdict);
		Assert.Equal(JwtVerdict.Verified, Verify(token, TestTokens.EcJwk(ecdsa, curve)).Verdict);
		Assert.Equal(JwtVerdict.Failed, Verify(TestTokens.Tampered(token), ecdsa.ExportSubjectPublicKeyInfoPem()).Verdict);
	}

	[Fact]
	public void Ecdsa_SaysADerSignatureIsTheWrongShape()
	{
		using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		string token = TestTokens.Ecdsa("ES256", ecdsa, format: DSASignatureFormat.Rfc3279DerSequence);

		JwtVerification result = Verify(token, ecdsa.ExportSubjectPublicKeyInfoPem());

		Assert.Equal(JwtVerdict.Failed, result.Verdict);
		Assert.Contains("DER", result.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void PublicKey_OfTheWrongKindIsNotChecked()
	{
		using ECDsa p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
		using ECDsa p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		using RSA rsa = RSA.Create(2048);

		JwtVerification curve = Verify(TestTokens.Ecdsa("ES256", p256), p384.ExportSubjectPublicKeyInfoPem());
		JwtVerification kind = Verify(TestTokens.Rsa("RS256", rsa), p256.ExportSubjectPublicKeyInfoPem());

		Assert.Equal(JwtVerdict.NotChecked, curve.Verdict);
		Assert.Contains("P-256", curve.Message, StringComparison.Ordinal);
		Assert.Equal(JwtVerdict.NotChecked, kind.Verdict);
		Assert.Contains("RSA", kind.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Families_AreNotMixed()
	{
		using RSA rsa = RSA.Create(2048);
		JwtToken hmac = JwtDecoder.Decode(TestTokens.Hmac("HS256", Secret));
		JwtToken signed = JwtDecoder.Decode(TestTokens.Rsa("RS256", rsa));
		using JwtPublicKey key = JwtKeyReader.Read(rsa.ExportSubjectPublicKeyInfoPem(), null);

		Assert.Equal(JwtVerdict.NotChecked, JwtVerifier.VerifyPublicKey(hmac, key).Verdict);
		Assert.Equal(JwtVerdict.NotChecked, JwtVerifier.VerifyHmac(signed, Secret).Verdict);
		Assert.Equal(JwtFamily.Hmac, JwtVerifier.FamilyOf("HS384"));
		Assert.Equal(JwtFamily.PublicKey, JwtVerifier.FamilyOf("PS512"));
		Assert.Equal(JwtFamily.Unsupported, JwtVerifier.FamilyOf("EdDSA"));
		Assert.Equal(JwtFamily.Unsupported, JwtVerifier.FamilyOf(null));
	}

	[Fact]
	public void AlgNone_IsNeverChecked()
	{
		JwtToken token = JwtDecoder.Decode($"{TestTokens.Part("""{"alg":"none"}""")}.{TestTokens.Part(TestTokens.Payload)}.");
		using RSA rsa = RSA.Create(2048);
		using JwtPublicKey key = JwtKeyReader.Read(rsa.ExportSubjectPublicKeyInfoPem(), null);

		Assert.True(token.IsUnsigned);
		Assert.Equal(JwtFamily.None, JwtVerifier.FamilyOf(token.Algorithm));
		Assert.Equal(JwtVerdict.NotChecked, JwtVerifier.VerifyHmac(token, Secret).Verdict);
		Assert.Equal(JwtVerdict.NotChecked, JwtVerifier.VerifyPublicKey(token, key).Verdict);
		Assert.Contains("not signed", JwtVerifier.VerifyHmac(token, Secret).Message, StringComparison.Ordinal);
	}

	[Fact]
	public void KeyReader_RefusesPrivateKeys()
	{
		using RSA rsa = RSA.Create(2048);
		using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
		string privateJwk = TestTokens.RsaJwk(rsa).TrimEnd('}') + ",\"d\":\"AQAB\"}";

		Assert.Contains("private key", Assert.Throws<JwtFormatException>(() => JwtKeyReader.Read(rsa.ExportPkcs8PrivateKeyPem(), null)).Message, StringComparison.Ordinal);
		Assert.Contains("private key", Assert.Throws<JwtFormatException>(() => JwtKeyReader.Read(rsa.ExportRSAPrivateKeyPem(), null)).Message, StringComparison.Ordinal);
		Assert.Contains("private key", Assert.Throws<JwtFormatException>(() => JwtKeyReader.Read(ecdsa.ExportECPrivateKeyPem(), null)).Message, StringComparison.Ordinal);
		Assert.Contains("private key", Assert.Throws<JwtFormatException>(() => JwtKeyReader.Read(privateJwk, null)).Message, StringComparison.Ordinal);
	}

	[Fact]
	public void KeyReader_PicksFromASetOnlyWhenItCan()
	{
		using RSA first = RSA.Create(2048);
		using RSA second = RSA.Create(2048);
		string set = $$"""{"keys":[{{TestTokens.RsaJwk(first, "a")}},{{TestTokens.RsaJwk(second, "b")}}]}""";

		Assert.Contains("names no kid", Assert.Throws<JwtFormatException>(() => JwtKeyReader.Read(set, null)).Message, StringComparison.Ordinal);
		Assert.Contains("(c)", Assert.Throws<JwtFormatException>(() => JwtKeyReader.Read(set, "c")).Message, StringComparison.Ordinal);
		Assert.Throws<JwtFormatException>(() => JwtKeyReader.Read("not a key", null));
		Assert.Contains("shared secret", Assert.Throws<JwtFormatException>(() => JwtKeyReader.Read("""{"kty":"oct","k":"c2VjcmV0"}""", null)).Message, StringComparison.Ordinal);
	}

	private static JwtVerification Verify(string token, string key)
	{
		JwtToken read = JwtDecoder.Decode(token);
		using JwtPublicKey publicKey = JwtKeyReader.Read(key, read.KeyId);
		return JwtVerifier.VerifyPublicKey(read, publicKey);
	}
}
