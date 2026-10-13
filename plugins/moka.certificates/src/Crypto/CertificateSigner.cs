using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;

namespace Moka.Certificates.Crypto;

/// <summary>
/// Signs and verifies: raw signatures over data with an RSA or ECDSA key (the same bytes <c>openssl dgst -sign</c> makes,
/// DER for ECDSA), and CMS (PKCS#7) signatures, detached or with the content inside.
/// </summary>
public static class CertificateSigner
{
	public static byte[] Sign(ReadOnlySpan<byte> data, AsymmetricAlgorithm key, SignatureHash hash, RsaSignatureScheme scheme)
	{
		ArgumentNullException.ThrowIfNull(key);
		HashAlgorithmName name = CryptoChoices.Hash(hash);
		return key switch
		{
			RSA rsa => rsa.SignData(data, name, Padding(scheme)),
			ECDsa ecdsa => ecdsa.SignData(data, name, DSASignatureFormat.Rfc3279DerSequence),
			_ => throw new CertificateException("Only RSA and ECDSA keys sign here."),
		};
	}

	/// <summary>
	/// Whether <paramref name="signature"/> is <paramref name="key"/>'s signature over <paramref name="data"/>. An ECDSA
	/// signature is taken in DER (OpenSSL's form) or as the two numbers side by side (the form JOSE and Windows use).
	/// </summary>
	/// <exception cref="CertificateException">The key is not RSA or ECDSA.</exception>
	public static bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, PublicKey key, SignatureHash hash, RsaSignatureScheme scheme)
	{
		ArgumentNullException.ThrowIfNull(key);
		HashAlgorithmName name = CryptoChoices.Hash(hash);
		try
		{
			switch (key.Oid.Value)
			{
				case OidNames.RsaKey:
				{
					using RSA rsa = key.GetRSAPublicKey() ?? throw new CertificateException("The RSA key could not be loaded.");
					return rsa.VerifyData(data, signature, name, Padding(scheme));
				}

				case OidNames.EcKey:
				{
					using ECDsa ecdsa = key.GetECDsaPublicKey() ?? throw new CertificateException("The EC key could not be loaded.");
					return ecdsa.VerifyData(data, signature, name, DSASignatureFormat.Rfc3279DerSequence)
						|| (signature.Length == ecdsa.GetMaxSignatureSize(DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
							&& ecdsa.VerifyData(data, signature, name, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
				}

				default:
					throw new CertificateException(CertificateReader.DescribeKey(key).Limitation ?? "This key cannot verify signatures here.");
			}
		}
		catch (CryptographicException)
		{
			// A signature that is not even shaped right for the key is simply not its signature.
			return false;
		}
	}

	/// <summary>A CMS SignedData over <paramref name="content"/>, signed by <paramref name="certificate"/>'s key.</summary>
	/// <param name="content">What is signed.</param>
	/// <param name="certificate">The signer, which is included in the signature.</param>
	/// <param name="key">The signer's private key.</param>
	/// <param name="hash">The digest.</param>
	/// <param name="detached">Leave the content out, so the signature travels beside it.</param>
	/// <param name="extraCertificates">More certificates to include, such as the signer's chain.</param>
	/// <param name="now">The signing time recorded in the signature.</param>
	/// <exception cref="CertificateException">The key does not belong to the certificate.</exception>
	public static byte[] SignCms(
		ReadOnlySpan<byte> content,
		X509Certificate2 certificate,
		AsymmetricAlgorithm key,
		SignatureHash hash,
		bool detached,
		IEnumerable<X509Certificate2> extraCertificates,
		DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(certificate);
		ArgumentNullException.ThrowIfNull(key);
		ArgumentNullException.ThrowIfNull(extraCertificates);
		if (!KeyIdentity.Matches(certificate.PublicKey, new PublicKey(key)))
		{
			throw new CertificateException("The private key does not belong to the signer's certificate.");
		}

		SignedCms cms = new(new ContentInfo(content.ToArray()), detached);
		CmsSigner signer = new(SubjectIdentifierType.IssuerAndSerialNumber, certificate, key)
		{
			DigestAlgorithm = new Oid(DigestOid(hash)),
			IncludeOption = X509IncludeOption.EndCertOnly,
		};

		signer.SignedAttributes.Add(new Pkcs9SigningTime(now.UtcDateTime));
		foreach (X509Certificate2 extra in extraCertificates)
		{
			if (!extra.RawData.AsSpan().SequenceEqual(certificate.RawData))
			{
				signer.Certificates.Add(extra);
			}
		}

		try
		{
			cms.ComputeSignature(signer, silent: true);
		}
		catch (CryptographicException ex)
		{
			throw new CertificateException("The signature could not be made with this key and certificate.", ex);
		}

		return cms.Encode();
	}

	/// <summary>Checks every signature on a CMS SignedData against its signer's key.</summary>
	/// <param name="cms">The signature, as DER.</param>
	/// <param name="detachedContent">The content a detached signature covers; null for an attached one.</param>
	/// <param name="expectedSigner">A certificate the signer must be, and the one to check with when the signature leaves it out.</param>
	/// <exception cref="CertificateException">It is not a CMS signature, or it is detached and no content was given.</exception>
	public static CmsVerification VerifyCms(byte[] cms, byte[]? detachedContent, X509Certificate2? expectedSigner)
	{
		ArgumentNullException.ThrowIfNull(cms);
		SignedCms signed = detachedContent is null ? new SignedCms() : new SignedCms(new ContentInfo(detachedContent), detached: true);
		try
		{
			signed.Decode(cms);
		}
		catch (CryptographicException ex)
		{
			throw new CertificateException("This is not a CMS (PKCS#7) signature.", ex);
		}

		if (detachedContent is null && IsDetached(cms))
		{
			throw new CertificateException("This signature is detached: give the content it was made over.");
		}

		if (signed.SignerInfos.Count == 0)
		{
			throw new CertificateException("This CMS structure holds no signatures.");
		}

		List<CmsSignerResult> results = [];
		foreach (SignerInfo info in signed.SignerInfos)
		{
			results.Add(Check(info, expectedSigner));
		}

		bool allValid = results.All(result => result.Valid);
		bool expectedFound = expectedSigner is null || results.Any(result => result.IsExpectedSigner == true);
		return new CmsVerification
		{
			Valid = allValid && expectedFound,
			Detached = detachedContent is not null,
			Signers = results,
			Content = detachedContent is null ? signed.ContentInfo.Content.ToArray() : null,
		};
	}

	private static CmsSignerResult Check(SignerInfo info, X509Certificate2? expected)
	{
		X509Certificate2? certificate = info.Certificate;
		bool useExpected = certificate is null && expected is not null && Identifies(info.SignerIdentifier, expected);
		X509Certificate2? signerCertificate = certificate ?? (useExpected ? expected : null);
		bool valid;
		string? problem = null;
		try
		{
			if (useExpected)
			{
				info.CheckSignature([expected!], verifySignatureOnly: true);
			}
			else
			{
				info.CheckSignature(verifySignatureOnly: true);
			}

			valid = true;
		}
		catch (CryptographicException)
		{
			valid = false;
			problem = signerCertificate is null
				? "The signer's certificate is not inside the signature. Give it as the expected signer to check with it."
				: "The signature does not match the content, or was not made with the signer's key.";
		}

		DateTimeOffset? signingTime = null;
		foreach (CryptographicAttributeObject attribute in info.SignedAttributes)
		{
			if (attribute.Oid?.Value == "1.2.840.113549.1.9.5" && attribute.Values.Count > 0)
			{
				signingTime = new DateTimeOffset(new Pkcs9SigningTime(attribute.Values[0].RawData).SigningTime.ToUniversalTime(), TimeSpan.Zero);
			}
		}

		bool? isExpected = expected is null
			? null
			: signerCertificate is not null && signerCertificate.RawData.AsSpan().SequenceEqual(expected.RawData);
		if (isExpected == false && valid)
		{
			problem = "The signature holds, but it was made by someone other than the expected signer.";
		}

		return new CmsSignerResult
		{
			Signer = signerCertificate?.SubjectName.Name ?? Describe(info.SignerIdentifier),
			CertificateSha256 = signerCertificate is null ? null : BinaryText.Colons(SHA256.HashData(signerCertificate.RawData)),
			SigningTime = signingTime,
			DigestAlgorithm = DigestName(info.DigestAlgorithm.Value),
			Valid = valid,
			Problem = problem,
			IsExpectedSigner = isExpected,
		};
	}

	/// <summary>
	/// Whether a SignedData carries no content of its own. Decoding does not say: a detached signature decodes with empty
	/// content, which would then simply fail to verify.
	/// </summary>
	internal static bool IsDetached(byte[] cms)
	{
		try
		{
			AsnReader contentInfo = new AsnReader(cms, AsnEncodingRules.BER).ReadSequence();
			contentInfo.ReadObjectIdentifier();
			AsnReader signedData = contentInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)).ReadSequence();
			signedData.ReadInteger();
			signedData.ReadSetOf();
			AsnReader encapsulated = signedData.ReadSequence();
			encapsulated.ReadObjectIdentifier();
			return !encapsulated.HasData;
		}
		catch (AsnContentException)
		{
			return false;
		}
	}

	/// <summary>Whether a CMS recipient or signer identifier names <paramref name="certificate"/>.</summary>
	internal static bool Identifies(SubjectIdentifier identifier, X509Certificate2 certificate)
	{
		switch (identifier.Type)
		{
			case SubjectIdentifierType.IssuerAndSerialNumber when identifier.Value is X509IssuerSerial issuerSerial:
				return string.Equals(issuerSerial.SerialNumber, certificate.SerialNumber, StringComparison.OrdinalIgnoreCase)
					&& string.Equals(issuerSerial.IssuerName, certificate.IssuerName.Name, StringComparison.Ordinal);
			case SubjectIdentifierType.SubjectKeyIdentifier when identifier.Value is string keyId:
				return certificate.Extensions.OfType<X509SubjectKeyIdentifierExtension>().FirstOrDefault() is { SubjectKeyIdentifier: { } own }
					&& string.Equals(own, keyId, StringComparison.OrdinalIgnoreCase);
			default:
				return false;
		}
	}

	internal static string Describe(SubjectIdentifier identifier) => identifier.Value switch
	{
		X509IssuerSerial issuerSerial => $"Serial {issuerSerial.SerialNumber} from {issuerSerial.IssuerName}",
		string keyId => $"Key ID {keyId}",
		_ => "An unnamed signer",
	};

	private static RSASignaturePadding Padding(RsaSignatureScheme scheme) => scheme == RsaSignatureScheme.Pss ? RSASignaturePadding.Pss : RSASignaturePadding.Pkcs1;

	private static string DigestOid(SignatureHash hash) => hash switch
	{
		SignatureHash.Sha384 => OidNames.Sha384,
		SignatureHash.Sha512 => OidNames.Sha512,
		_ => OidNames.Sha256,
	};

	private static string DigestName(string? oid) => oid switch
	{
		OidNames.Sha256 => "SHA-256",
		OidNames.Sha384 => "SHA-384",
		OidNames.Sha512 => "SHA-512",
		OidNames.Sha1 => "SHA-1",
		_ => oid ?? "unknown",
	};
}
