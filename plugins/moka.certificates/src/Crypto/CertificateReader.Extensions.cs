using System.Formats.Asn1;
using System.Globalization;
using System.Net;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Crypto;

public static partial class CertificateReader
{
	private static readonly Asn1Tag[] GeneralNameStringTags =
	[
		new(TagClass.ContextSpecific, 1),
		new(TagClass.ContextSpecific, 2),
		new(TagClass.ContextSpecific, 6),
	];

	/// <summary>
	/// One extension in words. The names of a subject alternative names extension also land in <paramref name="names"/>,
	/// and basic constraints set <paramref name="isCa"/>. An extension that does not decode says so instead of failing
	/// the whole certificate.
	/// </summary>
	private static ExtensionInfo DescribeExtension(X509Extension extension, List<GeneralNameInfo> names, ref bool isCa)
	{
		string oid = extension.Oid?.Value ?? "";
		string name = OidNames.Name(OidNames.Extensions, oid);
		List<string> lines = [];
		try
		{
			byte[] raw = extension.RawData;
			switch (oid)
			{
				case OidNames.BasicConstraints:
				{
					X509BasicConstraintsExtension constraints = new(extension, extension.Critical);
					isCa = constraints.CertificateAuthority;
					lines.Add(constraints.CertificateAuthority ? "A certificate authority (CA:TRUE)" : "Not a certificate authority (CA:FALSE)");
					if (constraints.HasPathLengthConstraint)
					{
						lines.Add(string.Create(CultureInfo.InvariantCulture, $"At most {constraints.PathLengthConstraint} CAs below it"));
					}

					break;
				}

				case OidNames.KeyUsage:
					lines.AddRange(KeyUsageWords(new X509KeyUsageExtension(extension, extension.Critical).KeyUsages));
					break;
				case OidNames.ExtendedKeyUsage:
					foreach (Oid purpose in new X509EnhancedKeyUsageExtension(extension, extension.Critical).EnhancedKeyUsages)
					{
						lines.Add(OidNames.NameWithNumber(OidNames.Purposes, purpose.Value));
					}

					break;
				case OidNames.SubjectKeyIdentifier:
					lines.Add(BinaryText.Colons(new X509SubjectKeyIdentifierExtension(extension, extension.Critical).SubjectKeyIdentifierBytes.Span));
					break;
				case OidNames.SubjectAltName:
				case OidNames.IssuerAltName:
				{
					List<GeneralNameInfo> found = ReadGeneralNames(new AsnReader(raw, AsnEncodingRules.BER).ReadSequence());
					if (oid == OidNames.SubjectAltName)
					{
						names.AddRange(found);
					}

					lines.AddRange(found.Select(entry => entry.ToString()));
					break;
				}

				case OidNames.AuthorityKeyIdentifier:
					lines.AddRange(AuthorityKeyIdentifierLines(raw));
					break;
				case OidNames.AuthorityInfoAccess:
					lines.AddRange(AccessLines(raw));
					break;
				case OidNames.CrlDistributionPoints:
					lines.AddRange(CrlLines(raw));
					break;
				case OidNames.CertificatePolicies:
					lines.AddRange(PolicyLines(raw));
					break;
				case OidNames.NameConstraints:
					lines.AddRange(NameConstraintLines(raw));
					break;
				case OidNames.TlsFeature:
					lines.AddRange(TlsFeatureLines(raw));
					break;
				case OidNames.SignedCertificateTimestamps:
					lines.Add(SctLine(raw));
					break;
				case OidNames.PrecertificatePoison:
					lines.Add("Marks a precertificate for Certificate Transparency; it is never used as a certificate.");
					break;
				default:
					lines.Add(string.Create(CultureInfo.InvariantCulture, $"{raw.Length} bytes, not described here"));
					break;
			}
		}
		catch (Exception ex) when (ex is AsnContentException or CryptographicException or ArgumentException)
		{
			lines.Clear();
			lines.Add("Could not be read.");
		}

		return new ExtensionInfo(name, oid, extension.Critical, lines);
	}

	private static List<string> KeyUsageWords(X509KeyUsageFlags usages)
	{
		(X509KeyUsageFlags Flag, string Word)[] words =
		[
			(X509KeyUsageFlags.DigitalSignature, "Digital signature"),
			(X509KeyUsageFlags.NonRepudiation, "Content commitment (non-repudiation)"),
			(X509KeyUsageFlags.KeyEncipherment, "Key encipherment"),
			(X509KeyUsageFlags.DataEncipherment, "Data encipherment"),
			(X509KeyUsageFlags.KeyAgreement, "Key agreement"),
			(X509KeyUsageFlags.KeyCertSign, "Certificate signing"),
			(X509KeyUsageFlags.CrlSign, "CRL signing"),
			(X509KeyUsageFlags.EncipherOnly, "Encipher only"),
			(X509KeyUsageFlags.DecipherOnly, "Decipher only"),
		];

		List<string> found = [.. words.Where(entry => usages.HasFlag(entry.Flag)).Select(entry => entry.Word)];
		return found.Count > 0 ? found : ["None"];
	}

	/// <summary>Reads a SEQUENCE OF GeneralName, positioned inside the sequence.</summary>
	internal static List<GeneralNameInfo> ReadGeneralNames(AsnReader reader)
	{
		List<GeneralNameInfo> names = [];
		while (reader.HasData)
		{
			names.Add(ReadGeneralName(reader));
		}

		return names;
	}

	private static GeneralNameInfo ReadGeneralName(AsnReader reader)
	{
		Asn1Tag tag = reader.PeekTag();
		if (tag.TagClass != TagClass.ContextSpecific)
		{
			reader.ReadEncodedValue();
			return new GeneralNameInfo(GeneralNameKind.Other, "(unreadable)");
		}

		switch (tag.TagValue)
		{
			case 0:
			{
				AsnReader other = reader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
				string type = other.ReadObjectIdentifier();
				string value = "";
				if (other.HasData)
				{
					AsnReader explicitValue = other.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
					value = TryReadString(explicitValue) ?? "(binary)";
				}

				return new GeneralNameInfo(GeneralNameKind.Other, type == OidNames.UserPrincipalName ? $"UPN {value}" : $"{type} {value}");
			}

			case 1:
				return new GeneralNameInfo(GeneralNameKind.Email, reader.ReadCharacterString(UniversalTagNumber.IA5String, GeneralNameStringTags[0]));
			case 2:
				return new GeneralNameInfo(GeneralNameKind.Dns, reader.ReadCharacterString(UniversalTagNumber.IA5String, GeneralNameStringTags[1]));
			case 4:
			{
				AsnReader directory = reader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 4, isConstructed: true));
				return new GeneralNameInfo(GeneralNameKind.DirectoryName, new X500DistinguishedName(directory.ReadEncodedValue().Span).Name);
			}

			case 6:
				return new GeneralNameInfo(GeneralNameKind.Uri, reader.ReadCharacterString(UniversalTagNumber.IA5String, GeneralNameStringTags[2]));
			case 7:
				return new GeneralNameInfo(GeneralNameKind.IpAddress, AddressText(reader.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 7))));
			case 8:
				return new GeneralNameInfo(GeneralNameKind.RegisteredId, reader.ReadObjectIdentifier(new Asn1Tag(TagClass.ContextSpecific, 8)));
			default:
				reader.ReadEncodedValue();
				return new GeneralNameInfo(GeneralNameKind.Other, string.Create(CultureInfo.InvariantCulture, $"[{tag.TagValue}]"));
		}
	}

	// An address, or in name constraints an address and its mask.
	private static string AddressText(byte[] bytes) => bytes.Length switch
	{
		4 or 16 => new IPAddress(bytes).ToString(),
		8 => $"{new IPAddress(bytes.AsSpan(0, 4))}/{new IPAddress(bytes.AsSpan(4, 4))}",
		32 => $"{new IPAddress(bytes.AsSpan(0, 16))}/{PrefixLength(bytes.AsSpan(16))}",
		_ => Convert.ToHexString(bytes),
	};

	private static int PrefixLength(ReadOnlySpan<byte> mask)
	{
		int bits = 0;
		foreach (byte part in mask)
		{
			bits += BitOperations.PopCount(part);
		}

		return bits;
	}

	private static List<string> AuthorityKeyIdentifierLines(byte[] raw)
	{
		List<string> lines = [];
		AsnReader sequence = new AsnReader(raw, AsnEncodingRules.BER).ReadSequence();
		while (sequence.HasData)
		{
			Asn1Tag tag = sequence.PeekTag();
			if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
			{
				lines.Add("Key ID " + BinaryText.Colons(sequence.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 0))));
			}
			else if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)))
			{
				AsnReader issuer = sequence.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 1, isConstructed: true));
				lines.AddRange(ReadGeneralNames(issuer).Select(entry => "Issuer " + entry));
			}
			else if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 2)))
			{
				lines.Add("Issuer serial " + Convert.ToHexString(sequence.ReadIntegerBytes(new Asn1Tag(TagClass.ContextSpecific, 2)).Span));
			}
			else
			{
				sequence.ReadEncodedValue();
			}
		}

		return lines;
	}

	private static List<string> AccessLines(byte[] raw)
	{
		List<string> lines = [];
		AsnReader sequence = new AsnReader(raw, AsnEncodingRules.BER).ReadSequence();
		while (sequence.HasData)
		{
			AsnReader description = sequence.ReadSequence();
			string method = description.ReadObjectIdentifier();
			GeneralNameInfo location = ReadGeneralName(description);
			string label = method switch
			{
				OidNames.Ocsp => "OCSP",
				OidNames.CaIssuers => "CA issuers",
				_ => method,
			};

			lines.Add($"{label}: {location.Value}");
		}

		return lines;
	}

	private static List<string> CrlLines(byte[] raw)
	{
		List<string> lines = [];
		AsnReader points = new AsnReader(raw, AsnEncodingRules.BER).ReadSequence();
		while (points.HasData)
		{
			AsnReader point = points.ReadSequence();
			while (point.HasData)
			{
				Asn1Tag tag = point.PeekTag();
				if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
				{
					AsnReader pointName = point.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
					if (pointName.HasData && pointName.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
					{
						AsnReader fullName = pointName.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
						lines.AddRange(ReadGeneralNames(fullName).Select(entry => entry.Value));
					}
					else
					{
						lines.Add("A name relative to the CRL issuer");
						break;
					}
				}
				else
				{
					point.ReadEncodedValue();
				}
			}
		}

		return lines;
	}

	private static List<string> PolicyLines(byte[] raw)
	{
		List<string> lines = [];
		AsnReader policies = new AsnReader(raw, AsnEncodingRules.BER).ReadSequence();
		while (policies.HasData)
		{
			AsnReader policy = policies.ReadSequence();
			lines.Add(OidNames.NameWithNumber(OidNames.Policies, policy.ReadObjectIdentifier()));
			if (!policy.HasData)
			{
				continue;
			}

			AsnReader qualifiers = policy.ReadSequence();
			while (qualifiers.HasData)
			{
				AsnReader qualifier = qualifiers.ReadSequence();
				string kind = qualifier.ReadObjectIdentifier();
				if (kind == OidNames.CpsQualifier && qualifier.HasData)
				{
					lines.Add("  CPS: " + qualifier.ReadCharacterString(UniversalTagNumber.IA5String));
				}
				else if (kind == OidNames.UserNoticeQualifier && qualifier.HasData)
				{
					AsnReader notice = qualifier.ReadSequence();
					while (notice.HasData)
					{
						if (notice.PeekTag().HasSameClassAndValue(Asn1Tag.Sequence))
						{
							notice.ReadEncodedValue();
						}
						else if (TryReadString(notice) is { } text)
						{
							lines.Add("  Notice: " + text);
						}
						else
						{
							notice.ReadEncodedValue();
						}
					}
				}
			}
		}

		return lines;
	}

	private static List<string> NameConstraintLines(byte[] raw)
	{
		List<string> lines = [];
		AsnReader constraints = new AsnReader(raw, AsnEncodingRules.BER).ReadSequence();
		while (constraints.HasData)
		{
			Asn1Tag tag = constraints.PeekTag();
			string label = tag.TagValue == 0 ? "Permitted" : "Excluded";
			AsnReader subtrees = constraints.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, tag.TagValue, isConstructed: true));
			while (subtrees.HasData)
			{
				AsnReader subtree = subtrees.ReadSequence();
				GeneralNameInfo name = ReadGeneralName(subtree);
				lines.Add($"{label}: {name}");
			}
		}

		return lines;
	}

	private static List<string> TlsFeatureLines(byte[] raw)
	{
		List<string> lines = [];
		AsnReader features = new AsnReader(raw, AsnEncodingRules.BER).ReadSequence();
		while (features.HasData)
		{
			BigInteger feature = features.ReadInteger();
			lines.Add(feature == 5 ? "OCSP must-staple (status_request)" : feature == 17 ? "status_request_v2" : string.Create(CultureInfo.InvariantCulture, $"Feature {feature}"));
		}

		return lines;
	}

	// The list is a TLS structure inside an OCTET STRING: a two-byte total length, then each timestamp with a two-byte
	// length of its own.
	private static string SctLine(byte[] raw)
	{
		byte[] list = new AsnReader(raw, AsnEncodingRules.BER).ReadOctetString();
		int count = 0;
		int position = 2;
		while (position + 2 <= list.Length)
		{
			int length = (list[position] << 8) | list[position + 1];
			position += 2 + length;
			count++;
		}

		return count == 1 ? "1 timestamp from a Certificate Transparency log" : string.Create(CultureInfo.InvariantCulture, $"{count} timestamps from Certificate Transparency logs");
	}

	private static string? TryReadString(AsnReader reader)
	{
		Asn1Tag tag = reader.PeekTag();
		if (tag.TagClass != TagClass.Universal)
		{
			return null;
		}

		return (UniversalTagNumber)tag.TagValue switch
		{
			UniversalTagNumber.UTF8String => reader.ReadCharacterString(UniversalTagNumber.UTF8String),
			UniversalTagNumber.IA5String => reader.ReadCharacterString(UniversalTagNumber.IA5String),
			UniversalTagNumber.VisibleString => reader.ReadCharacterString(UniversalTagNumber.VisibleString),
			UniversalTagNumber.PrintableString => reader.ReadCharacterString(UniversalTagNumber.PrintableString),
			UniversalTagNumber.BMPString => reader.ReadCharacterString(UniversalTagNumber.BMPString),
			_ => null,
		};
	}
}
