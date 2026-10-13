using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Moka.Certificates.Crypto;

namespace Moka.Certificates.Tests.Crypto;

public sealed class CertificateEncryptorTests
{
	[Theory]
	[InlineData(OaepHash.Sha256)]
	[InlineData(OaepHash.Sha1)]
	public void Oaep_RoundTrips(OaepHash hash)
	{
		using RSA key = TestMaterial.RsaKey();
		byte[] secret = Encoding.UTF8.GetBytes("a database password");

		byte[] ciphertext = CertificateEncryptor.EncryptOaep(secret, new PublicKey(key), hash);

		Assert.Equal(256, ciphertext.Length);
		Assert.Equal(secret, CertificateEncryptor.DecryptOaep(ciphertext, key, hash));
	}

	[Fact]
	public void Oaep_RefusesTooMuchDataTheWrongKeyAndEc()
	{
		using RSA key = TestMaterial.RsaKey();
		using RSA other = TestMaterial.RsaKey();
		using ECDsa ec = TestMaterial.EcKey();
		PublicKey publicKey = new(key);
		int max = CertificateEncryptor.MaxOaepBytes(publicKey, OaepHash.Sha256);
		byte[] ciphertext = CertificateEncryptor.EncryptOaep(new byte[max], publicKey, OaepHash.Sha256);

		Assert.Equal(190, max);
		Assert.Contains("at most 190 bytes", Assert.Throws<CertificateException>(() => CertificateEncryptor.EncryptOaep(new byte[max + 1], publicKey, OaepHash.Sha256)).Message, StringComparison.Ordinal);
		Assert.Throws<CertificateException>(() => CertificateEncryptor.DecryptOaep(ciphertext, other, OaepHash.Sha256));
		Assert.Throws<CertificateException>(() => CertificateEncryptor.EncryptOaep([1], new PublicKey(ec), OaepHash.Sha256));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Cms_EveryRecipientCanOpenIt(bool pkcs1)
	{
		using RSA aliceKey = TestMaterial.RsaKey();
		using RSA bobKey = TestMaterial.RsaKey();
		using X509Certificate2 alice = TestMaterial.SelfSigned(aliceKey, "alice.example");
		using X509Certificate2 bob = TestMaterial.SelfSigned(bobKey, "bob.example");
		byte[] content = Encoding.UTF8.GetBytes(new string('x', 5000));

		byte[] message = CertificateEncryptor.EncryptCms(content, [alice, bob], pkcs1);

		Assert.Equal(2, CertificateEncryptor.RecipientsOf(message).Count);
		Assert.Equal(content, CertificateEncryptor.DecryptCms(message, alice, aliceKey).Content);
		CmsDecryption forBob = CertificateEncryptor.DecryptCms(message, bob, bobKey);
		Assert.Equal(content, forBob.Content);
		Assert.Equal(2, forBob.RecipientCount);
	}

	[Fact]
	public void Cms_RefusesStrangersAndEcRecipients()
	{
		using RSA aliceKey = TestMaterial.RsaKey();
		using RSA eveKey = TestMaterial.RsaKey();
		using ECDsa ecKey = TestMaterial.EcKey();
		using X509Certificate2 alice = TestMaterial.SelfSigned(aliceKey, "alice.example");
		using X509Certificate2 eve = TestMaterial.SelfSigned(eveKey, "eve.example");
		using X509Certificate2 ec = TestMaterial.SelfSigned(ecKey, "ec.example");
		byte[] message = CertificateEncryptor.EncryptCms([1, 2, 3], [alice], pkcs1KeyTransport: false);

		Assert.Contains("not addressed", Assert.Throws<CertificateException>(() => CertificateEncryptor.DecryptCms(message, eve, eveKey)).Message, StringComparison.Ordinal);
		Assert.Contains("does not belong", Assert.Throws<CertificateException>(() => CertificateEncryptor.DecryptCms(message, alice, eveKey)).Message, StringComparison.Ordinal);
		Assert.Contains("RSA certificates only", Assert.Throws<CertificateException>(() => CertificateEncryptor.EncryptCms([1], [ec], false)).Message, StringComparison.Ordinal);
		Assert.Throws<CertificateException>(() => CertificateEncryptor.EncryptCms([1], [], false));
		Assert.Throws<CertificateException>(() => CertificateEncryptor.RecipientsOf([0x30, 0x00]));
	}
}
