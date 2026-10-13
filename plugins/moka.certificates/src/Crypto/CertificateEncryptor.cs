using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Crypto;

/// <summary>
/// Encrypts and decrypts: RSA-OAEP straight to a key, for a few bytes such as a password or a symmetric key, and CMS
/// enveloped data for anything longer, to one or more certificates (AES-256-CBC, the key wrapped with RSA for each).
/// </summary>
public static class CertificateEncryptor
{
	/// <summary>How many bytes RSA-OAEP can take with this key and hash: the key's size less twice the hash and two bytes.</summary>
	public static int MaxOaepBytes(PublicKey key, OaepHash hash)
	{
		ArgumentNullException.ThrowIfNull(key);
		using RSA rsa = RsaOf(key);
		return (rsa.KeySize / 8) - (2 * HashLength(hash)) - 2;
	}

	/// <exception cref="CertificateException">The key is not RSA, or the data is too long for it.</exception>
	public static byte[] EncryptOaep(ReadOnlySpan<byte> data, PublicKey key, OaepHash hash)
	{
		ArgumentNullException.ThrowIfNull(key);
		using RSA rsa = RsaOf(key);
		int max = (rsa.KeySize / 8) - (2 * HashLength(hash)) - 2;
		if (data.Length > max)
		{
			throw new CertificateException($"RSA-OAEP takes at most {max} bytes with this key, and this is {data.Length}. Use CMS for anything longer.");
		}

		return rsa.Encrypt(data, Padding(hash));
	}

	/// <exception cref="CertificateException">The key is not RSA, or it cannot decrypt this.</exception>
	public static byte[] DecryptOaep(ReadOnlySpan<byte> ciphertext, AsymmetricAlgorithm key, OaepHash hash)
	{
		if (key is not RSA rsa)
		{
			throw new CertificateException("RSA-OAEP needs an RSA key.");
		}

		try
		{
			return rsa.Decrypt(ciphertext, Padding(hash));
		}
		catch (CryptographicException ex)
		{
			throw new CertificateException("This key cannot decrypt it: it was encrypted to another key, or with the other OAEP hash.", ex);
		}
	}

	/// <summary>A CMS enveloped message that every certificate in <paramref name="recipients"/> can open with its key.</summary>
	/// <param name="content">What is encrypted.</param>
	/// <param name="recipients">RSA certificates; each gets its own copy of the content key.</param>
	/// <param name="pkcs1KeyTransport">Wrap the content key with RSA PKCS#1 v1.5, which old mail clients need, instead of OAEP.</param>
	/// <exception cref="CertificateException">There is no recipient, or one is not RSA.</exception>
	public static byte[] EncryptCms(ReadOnlySpan<byte> content, IReadOnlyList<X509Certificate2> recipients, bool pkcs1KeyTransport)
	{
		ArgumentNullException.ThrowIfNull(recipients);
		if (recipients.Count == 0)
		{
			throw new CertificateException("Give at least one recipient certificate.");
		}

		CmsRecipientCollection collection = [];
		foreach (X509Certificate2 recipient in recipients)
		{
			if (recipient.PublicKey.Oid.Value != OidNames.RsaKey)
			{
				throw new CertificateException($"{recipient.SubjectName.Name} has a {CertificateReader.DescribeKey(recipient.PublicKey).Algorithm} key. CMS encryption here works with RSA certificates only.");
			}

			collection.Add(new CmsRecipient(
				SubjectIdentifierType.IssuerAndSerialNumber,
				recipient,
				pkcs1KeyTransport ? RSAEncryptionPadding.Pkcs1 : RSAEncryptionPadding.OaepSHA256));
		}

		EnvelopedCms cms = new(new ContentInfo(content.ToArray()), new AlgorithmIdentifier(new Oid(OidNames.Aes256Cbc)));
		try
		{
			cms.Encrypt(collection);
		}
		catch (CryptographicException ex)
		{
			throw new CertificateException("The message could not be encrypted to these certificates.", ex);
		}

		return cms.Encode();
	}

	/// <summary>Who a CMS enveloped message is for, in words.</summary>
	/// <exception cref="CertificateException">It is not CMS enveloped data.</exception>
	public static IReadOnlyList<string> RecipientsOf(byte[] enveloped)
	{
		EnvelopedCms cms = Decode(enveloped);
		return [.. cms.RecipientInfos.Cast<RecipientInfo>().Select(recipient => CertificateSigner.Describe(recipient.RecipientIdentifier))];
	}

	/// <summary>Opens a CMS enveloped message with <paramref name="certificate"/>'s key.</summary>
	/// <exception cref="CertificateException">It is not addressed to the certificate, or the key cannot open it.</exception>
	public static CmsDecryption DecryptCms(byte[] enveloped, X509Certificate2 certificate, AsymmetricAlgorithm key)
	{
		ArgumentNullException.ThrowIfNull(certificate);
		ArgumentNullException.ThrowIfNull(key);
		if (!KeyIdentity.Matches(certificate.PublicKey, new PublicKey(key)))
		{
			throw new CertificateException("The private key does not belong to the certificate.");
		}

		EnvelopedCms cms = Decode(enveloped);
		RecipientInfo? recipient = cms.RecipientInfos.Cast<RecipientInfo>().FirstOrDefault(info => CertificateSigner.Identifies(info.RecipientIdentifier, certificate));
		if (recipient is null)
		{
			throw new CertificateException("This message is not addressed to that certificate.");
		}

		try
		{
			cms.Decrypt(recipient, key);
		}
		catch (CryptographicException ex)
		{
			throw new CertificateException("The key could not open the message.", ex);
		}

		return new CmsDecryption(cms.ContentInfo.Content.ToArray(), cms.RecipientInfos.Count);
	}

	private static EnvelopedCms Decode(byte[] enveloped)
	{
		ArgumentNullException.ThrowIfNull(enveloped);
		EnvelopedCms cms = new();
		try
		{
			cms.Decode(enveloped);
		}
		catch (CryptographicException ex)
		{
			throw new CertificateException("This is not a CMS encrypted message.", ex);
		}

		return cms;
	}

	private static RSA RsaOf(PublicKey key) =>
		key.Oid.Value == OidNames.RsaKey && key.GetRSAPublicKey() is { } rsa
			? rsa
			: throw new CertificateException("RSA-OAEP needs an RSA key or certificate.");

	private static int HashLength(OaepHash hash) => hash == OaepHash.Sha1 ? 20 : 32;

	private static RSAEncryptionPadding Padding(OaepHash hash) => hash == OaepHash.Sha1 ? RSAEncryptionPadding.OaepSHA1 : RSAEncryptionPadding.OaepSHA256;
}
