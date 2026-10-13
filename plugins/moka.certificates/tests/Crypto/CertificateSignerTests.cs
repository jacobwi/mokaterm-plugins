using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Moka.Certificates.Crypto;

namespace Moka.Certificates.Tests.Crypto;

public sealed class CertificateSignerTests
{
	private static readonly byte[] Data = Encoding.UTF8.GetBytes("Release 1.0.0 of the thing\n");

	[Theory]
	[InlineData(true, RsaSignatureScheme.Pkcs1, SignatureHash.Sha256)]
	[InlineData(true, RsaSignatureScheme.Pss, SignatureHash.Sha384)]
	[InlineData(true, RsaSignatureScheme.Pkcs1, SignatureHash.Sha512)]
	[InlineData(false, RsaSignatureScheme.Pkcs1, SignatureHash.Sha256)]
	[InlineData(false, RsaSignatureScheme.Pss, SignatureHash.Sha512)]
	public void Sign_ThenVerify_HoldsAndCatchesTampering(bool rsa, RsaSignatureScheme scheme, SignatureHash hash)
	{
		using AsymmetricAlgorithm key = rsa ? TestMaterial.RsaKey() : TestMaterial.EcKey();
		using AsymmetricAlgorithm other = rsa ? TestMaterial.RsaKey() : TestMaterial.EcKey();
		PublicKey publicKey = new(key);

		byte[] signature = CertificateSigner.Sign(Data, key, hash, scheme);
		byte[] changed = [.. Data];
		changed[0] ^= 1;
		byte[] broken = [.. signature];
		broken[^1] ^= 1;

		Assert.True(CertificateSigner.Verify(Data, signature, publicKey, hash, scheme));
		Assert.False(CertificateSigner.Verify(changed, signature, publicKey, hash, scheme));
		Assert.False(CertificateSigner.Verify(Data, broken, publicKey, hash, scheme));
		Assert.False(CertificateSigner.Verify(Data, signature, new PublicKey(other), hash, scheme));
		Assert.False(CertificateSigner.Verify(Data, signature, publicKey, hash == SignatureHash.Sha256 ? SignatureHash.Sha512 : SignatureHash.Sha256, scheme));
	}

	[Fact]
	public void Verify_EcdsaTakesBothSignatureForms()
	{
		using ECDsa key = TestMaterial.EcKey();
		byte[] raw = key.SignData(Data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

		Assert.True(CertificateSigner.Verify(Data, raw, new PublicKey(key), SignatureHash.Sha256, RsaSignatureScheme.Pkcs1));
	}

	[Fact]
	public void Verify_RsaPkcs1SignatureIsNotAPssOne()
	{
		using RSA key = TestMaterial.RsaKey();
		byte[] signature = CertificateSigner.Sign(Data, key, SignatureHash.Sha256, RsaSignatureScheme.Pkcs1);

		Assert.False(CertificateSigner.Verify(Data, signature, new PublicKey(key), SignatureHash.Sha256, RsaSignatureScheme.Pss));
	}

	[Fact]
	public void Verify_AgainstACertificateKey()
	{
		using TestChain chain = TestMaterial.Chain(rsa: true);
		byte[] signature = CertificateSigner.Sign(Data, chain.LeafKey, SignatureHash.Sha256, RsaSignatureScheme.Pkcs1);

		Assert.True(CertificateSigner.Verify(Data, signature, chain.Leaf.PublicKey, SignatureHash.Sha256, RsaSignatureScheme.Pkcs1));
	}

	[Fact]
	public void CmsAttached_VerifiesAndCarriesTheContent()
	{
		using TestChain chain = TestMaterial.Chain();
		byte[] cms = CertificateSigner.SignCms(Data, chain.Leaf, chain.LeafKey, SignatureHash.Sha256, detached: false, [chain.Intermediate], TestMaterial.Now);

		CmsVerification result = CertificateSigner.VerifyCms(cms, null, chain.Leaf);

		Assert.True(result.Valid);
		Assert.False(result.Detached);
		Assert.Equal(Data, result.Content);
		CmsSignerResult signer = Assert.Single(result.Signers);
		Assert.Equal(chain.Leaf.SubjectName.Name, signer.Signer);
		Assert.Equal("SHA-256", signer.DigestAlgorithm);
		Assert.Equal(TestMaterial.Now, signer.SigningTime);
		Assert.True(signer.IsExpectedSigner);
	}

	[Fact]
	public void CmsAttached_ChangedContent_IsCaught()
	{
		using TestChain chain = TestMaterial.Chain(rsa: true);
		byte[] cms = CertificateSigner.SignCms(Data, chain.Leaf, chain.LeafKey, SignatureHash.Sha256, detached: false, [], TestMaterial.Now);
		byte[] tampered = CertificateReaderTests.Tamper(cms, "Release 1.0.0", "Release 6.6.6");

		CmsVerification result = CertificateSigner.VerifyCms(tampered, null, null);

		Assert.False(result.Valid);
		Assert.False(Assert.Single(result.Signers).Valid);
	}

	[Fact]
	public void CmsDetached_NeedsTheContentAndChecksIt()
	{
		using TestChain chain = TestMaterial.Chain();
		byte[] cms = CertificateSigner.SignCms(Data, chain.Leaf, chain.LeafKey, SignatureHash.Sha384, detached: true, [], TestMaterial.Now);

		Assert.True(CertificateSigner.VerifyCms(cms, Data, null).Valid);
		Assert.False(CertificateSigner.VerifyCms(cms, [.. Data, 0x20], null).Valid);
		Assert.Contains("detached", Assert.Throws<CertificateException>(() => CertificateSigner.VerifyCms(cms, null, null)).Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Cms_SomeoneElseAsTheExpectedSigner_IsNotValid()
	{
		using TestChain chain = TestMaterial.Chain();
		byte[] cms = CertificateSigner.SignCms(Data, chain.Leaf, chain.LeafKey, SignatureHash.Sha256, detached: false, [], TestMaterial.Now);

		CmsVerification result = CertificateSigner.VerifyCms(cms, null, chain.Root);

		Assert.False(result.Valid);
		Assert.True(result.Signers[0].Valid);
		Assert.False(result.Signers[0].IsExpectedSigner);
	}

	[Fact]
	public void SignCms_RefusesAKeyThatIsNotTheCertificates()
	{
		using TestChain chain = TestMaterial.Chain();

		Assert.Throws<CertificateException>(() => CertificateSigner.SignCms(Data, chain.Leaf, chain.RootKey, SignatureHash.Sha256, false, [], TestMaterial.Now));
		Assert.Throws<CertificateException>(() => CertificateSigner.VerifyCms([0x30, 0x00], null, null));
	}
}
