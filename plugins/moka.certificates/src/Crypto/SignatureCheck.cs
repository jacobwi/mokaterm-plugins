using System.Formats.Asn1;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Crypto;

/// <summary>
/// Checks the signature on a certificate or a signing request against a public key. Both are the same shape
/// (what is signed, the algorithm, the signature), so one check serves "did the next certificate in the bundle sign this
/// one" and "does this request's signature hold". Nothing is fetched and nothing is trusted: it is the signature and
/// the key, and nothing else.
/// </summary>
internal static class SignatureCheck
{
	/// <summary>The algorithm's number in a signed structure, or null when it cannot be read.</summary>
	public static string? AlgorithmOf(ReadOnlyMemory<byte> signedDer)
	{
		try
		{
			AsnReader body = new AsnReader(signedDer, AsnEncodingRules.BER).ReadSequence();
			body.ReadEncodedValue();
			return body.ReadSequence().ReadObjectIdentifier();
		}
		catch (AsnContentException)
		{
			return null;
		}
	}

	/// <summary>
	/// True when <paramref name="signerKey"/> made the signature on <paramref name="signedDer"/>, false when it did not,
	/// and null when the algorithm (Ed25519, DSA, unusual PSS parameters) is not one this check can verify.
	/// </summary>
	public static bool? Verify(ReadOnlyMemory<byte> signedDer, PublicKey signerKey)
	{
		ArgumentNullException.ThrowIfNull(signerKey);
		ReadOnlyMemory<byte> signed;
		string algorithm;
		ReadOnlyMemory<byte>? parameters;
		byte[] signature;
		try
		{
			AsnReader body = new AsnReader(signedDer, AsnEncodingRules.BER).ReadSequence();
			signed = body.ReadEncodedValue();
			AsnReader identifier = body.ReadSequence();
			algorithm = identifier.ReadObjectIdentifier();
			parameters = identifier.HasData ? identifier.ReadEncodedValue() : null;
			signature = body.ReadBitString(out _);
		}
		catch (AsnContentException)
		{
			return false;
		}

		try
		{
			return algorithm switch
			{
				OidNames.RsaSha1 => VerifyRsa(signerKey, signed, signature, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1),
				OidNames.RsaSha256 => VerifyRsa(signerKey, signed, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
				OidNames.RsaSha384 => VerifyRsa(signerKey, signed, signature, HashAlgorithmName.SHA384, RSASignaturePadding.Pkcs1),
				OidNames.RsaSha512 => VerifyRsa(signerKey, signed, signature, HashAlgorithmName.SHA512, RSASignaturePadding.Pkcs1),
				OidNames.RsaPss => PssHash(parameters) is { } hash ? VerifyRsa(signerKey, signed, signature, hash, RSASignaturePadding.Pss) : null,
				OidNames.EcdsaSha1 => VerifyEcdsa(signerKey, signed, signature, HashAlgorithmName.SHA1),
				OidNames.EcdsaSha256 => VerifyEcdsa(signerKey, signed, signature, HashAlgorithmName.SHA256),
				OidNames.EcdsaSha384 => VerifyEcdsa(signerKey, signed, signature, HashAlgorithmName.SHA384),
				OidNames.EcdsaSha512 => VerifyEcdsa(signerKey, signed, signature, HashAlgorithmName.SHA512),
				_ => null,
			};
		}
		catch (CryptographicException)
		{
			return null;
		}
	}

	private static bool? VerifyRsa(PublicKey key, ReadOnlyMemory<byte> signed, byte[] signature, HashAlgorithmName hash, RSASignaturePadding padding)
	{
		if (key.Oid.Value != OidNames.RsaKey)
		{
			return false;
		}

		using RSA? rsa = key.GetRSAPublicKey();
		return rsa?.VerifyData(signed.Span, signature, hash, padding);
	}

	private static bool? VerifyEcdsa(PublicKey key, ReadOnlyMemory<byte> signed, byte[] signature, HashAlgorithmName hash)
	{
		if (key.Oid.Value != OidNames.EcKey)
		{
			return false;
		}

		using ECDsa? ecdsa = key.GetECDsaPublicKey();
		return ecdsa?.VerifyData(signed.Span, signature, hash, DSASignatureFormat.Rfc3279DerSequence);
	}

	// .NET's PSS is MGF1 with the same hash and a salt as long as the hash, which is what nearly every signer uses. A
	// signature made with other parameters is reported as not checkable rather than as wrong.
	private static HashAlgorithmName? PssHash(ReadOnlyMemory<byte>? parameters)
	{
		if (parameters is not { } encoded)
		{
			return null;
		}

		try
		{
			AsnReader sequence = new AsnReader(encoded, AsnEncodingRules.BER).ReadSequence();
			string hashOid = OidNames.Sha1;
			string mgfHashOid = OidNames.Sha1;
			BigInteger salt = 20;
			if (sequence.HasData && sequence.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
			{
				hashOid = sequence.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)).ReadSequence().ReadObjectIdentifier();
			}

			if (sequence.HasData && sequence.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)))
			{
				AsnReader mgf = sequence.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true)).ReadSequence();
				if (mgf.ReadObjectIdentifier() != OidNames.Mgf1)
				{
					return null;
				}

				mgfHashOid = mgf.ReadSequence().ReadObjectIdentifier();
			}

			if (sequence.HasData && sequence.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 2)))
			{
				salt = sequence.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 2, isConstructed: true)).ReadInteger();
			}

			(HashAlgorithmName Name, int Length)? hash = hashOid switch
			{
				OidNames.Sha256 => (HashAlgorithmName.SHA256, 32),
				OidNames.Sha384 => (HashAlgorithmName.SHA384, 48),
				OidNames.Sha512 => (HashAlgorithmName.SHA512, 64),
				OidNames.Sha1 => (HashAlgorithmName.SHA1, 20),
				_ => null,
			};

			return hash is { } known && mgfHashOid == hashOid && salt == known.Length ? known.Name : null;
		}
		catch (AsnContentException)
		{
			return null;
		}
	}
}
