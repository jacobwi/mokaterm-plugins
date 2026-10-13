using System.Formats.Asn1;
using System.Numerics;

namespace Moka.Certificates.Crypto;

/// <summary>
/// Tells what a DER blob is from the shape of its outer structure, before any decoder is asked to read it. Every type
/// this tool reads is a SEQUENCE whose first two or three elements differ, so the shape decides; a blob with anything
/// after its outer SEQUENCE is unknown.
/// </summary>
public static class DerDetector
{
	private static readonly Asn1Tag ContextZero = new(TagClass.ContextSpecific, 0, isConstructed: true);

	public static DerType Identify(ReadOnlyMemory<byte> der)
	{
		try
		{
			AsnReader outer = new(der, AsnEncodingRules.BER);
			AsnReader body = outer.ReadSequence();
			if (outer.HasData || !body.HasData)
			{
				return DerType.Unknown;
			}

			Asn1Tag first = body.PeekTag();
			if (first.HasSameClassAndValue(Asn1Tag.Integer))
			{
				return FromInteger(body);
			}

			if (first.HasSameClassAndValue(Asn1Tag.Sequence))
			{
				return FromSequence(body);
			}

			if (first.HasSameClassAndValue(Asn1Tag.ObjectIdentifier))
			{
				return body.ReadObjectIdentifier() == OidNames.SignedData ? DerType.Pkcs7 : DerType.Unknown;
			}
		}
		catch (AsnContentException)
		{
			return DerType.Unknown;
		}

		return DerType.Unknown;
	}

	/// <summary>The PEM label the type is written under, or null for PFX, which has no PEM form.</summary>
	public static string? PemLabel(DerType type) => type switch
	{
		DerType.Certificate => "CERTIFICATE",
		DerType.CertificateRequest => "CERTIFICATE REQUEST",
		DerType.PrivateKey => "PRIVATE KEY",
		DerType.EncryptedPrivateKey => "ENCRYPTED PRIVATE KEY",
		DerType.RsaPrivateKey => "RSA PRIVATE KEY",
		DerType.EcPrivateKey => "EC PRIVATE KEY",
		DerType.PublicKey => "PUBLIC KEY",
		DerType.RsaPublicKey => "RSA PUBLIC KEY",
		DerType.Pkcs7 => "PKCS7",
		_ => null,
	};

	/// <summary>The usual file extension for the type in DER form.</summary>
	public static string FileExtension(DerType type) => type switch
	{
		DerType.Certificate => ".cer",
		DerType.CertificateRequest => ".csr",
		DerType.PublicKey or DerType.RsaPublicKey => ".pub.der",
		DerType.Pfx => ".pfx",
		DerType.Pkcs7 => ".p7b",
		DerType.Unknown => ".der",
		_ => ".key.der",
	};

	// PKCS#12 (version 3), PKCS#8 (version 0 or 1, then an AlgorithmIdentifier), SEC1 (version 1, then the key as an
	// OCTET STRING), PKCS#1 private (version 0, then the modulus), PKCS#1 public (the modulus, then the exponent).
	private static DerType FromInteger(AsnReader body)
	{
		BigInteger first = body.ReadInteger();
		if (!body.HasData)
		{
			return DerType.Unknown;
		}

		Asn1Tag second = body.PeekTag();
		if (first == 3 && second.HasSameClassAndValue(Asn1Tag.Sequence))
		{
			return DerType.Pfx;
		}

		if ((first == 0 || first == 1) && second.HasSameClassAndValue(Asn1Tag.Sequence))
		{
			body.ReadSequence();
			return body.HasData && body.PeekTag().HasSameClassAndValue(Asn1Tag.PrimitiveOctetString) ? DerType.PrivateKey : DerType.Unknown;
		}

		if (first == 1 && second.HasSameClassAndValue(Asn1Tag.PrimitiveOctetString))
		{
			return DerType.EcPrivateKey;
		}

		if (second.HasSameClassAndValue(Asn1Tag.Integer))
		{
			body.ReadInteger();
			if (!body.HasData)
			{
				return first > 1 ? DerType.RsaPublicKey : DerType.Unknown;
			}

			return first == 0 ? DerType.RsaPrivateKey : DerType.Unknown;
		}

		return DerType.Unknown;
	}

	// A signed structure (certificate or request), an encrypted PKCS#8 key, or a SubjectPublicKeyInfo.
	private static DerType FromSequence(AsnReader body)
	{
		AsnReader inner = body.ReadSequence();
		if (!body.HasData)
		{
			return DerType.Unknown;
		}

		Asn1Tag second = body.PeekTag();
		bool algorithmIdentifier = inner.HasData && inner.PeekTag().HasSameClassAndValue(Asn1Tag.ObjectIdentifier);
		if (algorithmIdentifier)
		{
			if (second.HasSameClassAndValue(Asn1Tag.PrimitiveBitString))
			{
				return DerType.PublicKey;
			}

			if (second.HasSameClassAndValue(Asn1Tag.PrimitiveOctetString))
			{
				return DerType.EncryptedPrivateKey;
			}

			return DerType.Unknown;
		}

		if (!second.HasSameClassAndValue(Asn1Tag.Sequence))
		{
			return DerType.Unknown;
		}

		// A certificate's TBSCertificate opens with an explicit [0] version or its serial and holds at least six parts; a
		// request's info is a version, a name, a key and the [0] attributes.
		if (!inner.HasData)
		{
			return DerType.Unknown;
		}

		if (inner.PeekTag() == ContextZero)
		{
			return DerType.Certificate;
		}

		int parts = 0;
		Asn1Tag last = default;
		while (inner.HasData)
		{
			last = inner.PeekTag();
			inner.ReadEncodedValue();
			parts++;
		}

		if (parts == 4 && last == ContextZero)
		{
			return DerType.CertificateRequest;
		}

		return parts >= 6 ? DerType.Certificate : DerType.Unknown;
	}
}
