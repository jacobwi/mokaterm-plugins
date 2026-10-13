using System.Formats.Asn1;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Crypto;

public static partial class CertificateReader
{
	/// <summary>Everything the views show about a certificate.</summary>
	/// <param name="certificate">The certificate.</param>
	/// <param name="now">The time expiry is judged against.</param>
	/// <param name="expiringDays">How close to its end a certificate counts as expiring.</param>
	public static CertificateInfo Describe(X509Certificate2 certificate, DateTimeOffset now, int expiringDays)
	{
		ArgumentNullException.ThrowIfNull(certificate);
		byte[] der = certificate.RawData;
		IReadOnlyList<NamePart> subjectParts = Parts(certificate.SubjectName);
		IReadOnlyList<NamePart> issuerParts = Parts(certificate.IssuerName);
		List<ExtensionInfo> extensions = [];
		List<GeneralNameInfo> names = [];
		bool isCa = false;
		foreach (X509Extension extension in certificate.Extensions)
		{
			extensions.Add(DescribeExtension(extension, names, ref isCa));
		}

		DateTimeOffset notBefore = new(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero);
		DateTimeOffset notAfter = new(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
		bool selfIssued = certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData);
		string signatureOid = certificate.SignatureAlgorithm.Value ?? "";
		return new CertificateInfo
		{
			Subject = NameText(certificate.SubjectName),
			SubjectParts = subjectParts,
			Issuer = NameText(certificate.IssuerName),
			IssuerParts = issuerParts,
			Title = TitleOf(subjectParts, names, NameText(certificate.SubjectName)),
			IssuerTitle = First(issuerParts, "CN") ?? First(issuerParts, "O") ?? NameText(certificate.IssuerName),
			SerialNumber = certificate.SerialNumber,
			Version = certificate.Version,
			NotBefore = notBefore,
			NotAfter = notAfter,
			Expiry = ExpiryOf(notBefore, notAfter, now, expiringDays),
			DaysLeft = DaysLeft(notAfter, now),
			AlternativeNames = names,
			Key = DescribeKey(certificate.PublicKey),
			SignatureAlgorithm = OidNames.Name(OidNames.SignatureAlgorithms, signatureOid),
			SignatureAlgorithmOid = signatureOid,
			Extensions = extensions,
			IsCertificateAuthority = isCa,
			IsSelfIssued = selfIssued,
			IsSelfSigned = selfIssued ? SignatureCheck.Verify(der, certificate.PublicKey) : false,
			Sha256 = BinaryText.Colons(SHA256.HashData(der)),
			Sha1 = BinaryText.Colons(Sha1Of(der)),
			Der = der,
		};
	}

	/// <summary>Everything the views show about a signing request.</summary>
	public static RequestInfo Describe(RequestMaterial request)
	{
		ArgumentNullException.ThrowIfNull(request);
		IReadOnlyList<NamePart> subjectParts = Parts(request.Request.SubjectName);
		List<ExtensionInfo> extensions = [];
		List<GeneralNameInfo> names = [];
		bool isCa = false;
		foreach (X509Extension extension in request.Request.CertificateExtensions)
		{
			extensions.Add(DescribeExtension(extension, names, ref isCa));
		}

		string subject = NameText(request.Request.SubjectName);
		return new RequestInfo
		{
			Subject = subject,
			SubjectParts = subjectParts,
			Title = TitleOf(subjectParts, names, subject),
			AlternativeNames = names,
			Key = DescribeKey(request.Request.PublicKey),
			SignatureAlgorithm = OidNames.Name(OidNames.SignatureAlgorithms, SignatureCheck.AlgorithmOf(request.Der)),
			SignatureValid = SignatureCheck.Verify(request.Der, request.Request.PublicKey),
			Extensions = extensions,
			Sha256 = BinaryText.Colons(SHA256.HashData(request.Der)),
			Der = request.Der,
		};
	}

	/// <summary>A public key's algorithm, size or curve, and its SHA-256 fingerprints.</summary>
	public static KeyInfo DescribeKey(PublicKey key)
	{
		ArgumentNullException.ThrowIfNull(key);
		byte[] hash = SHA256.HashData(key.ExportSubjectPublicKeyInfo());
		string oid = key.Oid.Value ?? "";
		string fingerprint = BinaryText.Colons(hash);
		string pin = Convert.ToBase64String(hash);
		switch (oid)
		{
			case OidNames.RsaKey:
			{
				int? bits = RsaBits(key);
				return new KeyInfo
				{
					Algorithm = "RSA",
					Bits = bits,
					Description = bits is { } size ? string.Create(CultureInfo.InvariantCulture, $"RSA {size}") : "RSA",
					SpkiSha256 = fingerprint,
					Pin = pin,
					Usable = bits >= 1024,
					Limitation = bits < 2048 ? "RSA keys under 2048 bits are too weak to trust." : null,
				};
			}

			case OidNames.EcKey:
			{
				string? curveOid = CurveOid(key);
				string curve = OidNames.Name(OidNames.Curves, curveOid);
				bool named = curveOid is OidNames.P256 or OidNames.P384 or OidNames.P521;
				return new KeyInfo
				{
					Algorithm = "ECDSA",
					Bits = curveOid switch
					{
						OidNames.P256 => 256,
						OidNames.P384 => 384,
						OidNames.P521 => 521,
						_ => null,
					},
					Curve = curve,
					Description = $"ECDSA {curve}",
					SpkiSha256 = fingerprint,
					Pin = pin,
					Usable = named,
					Limitation = named ? null : $"The curve {curve} is shown, but only P-256, P-384 and P-521 are used here.",
				};
			}

			default:
			{
				string name = oid switch
				{
					OidNames.Ed25519 => "Ed25519",
					OidNames.Ed448 => "Ed448",
					OidNames.X25519 => "X25519",
					OidNames.X448 => "X448",
					OidNames.DsaKey => "DSA",
					OidNames.RsaPssKey => "RSA-PSS",
					_ => key.Oid.FriendlyName is { Length: > 0 } friendly ? friendly : oid,
				};

				return new KeyInfo
				{
					Algorithm = name,
					Bits = oid is OidNames.Ed25519 or OidNames.X25519 ? 256 : oid is OidNames.Ed448 ? 456 : null,
					Description = name,
					SpkiSha256 = fingerprint,
					Pin = pin,
					Usable = false,
					Limitation = oid == OidNames.Ed25519
						? "Ed25519 is not supported in this version: the key is shown, not used."
						: $"{name} keys are shown, not used: this tool signs, verifies and encrypts with RSA and ECDSA.",
				};
			}
		}
	}

	/// <summary>Where a certificate with these dates stands at <paramref name="now"/>.</summary>
	public static ExpiryState ExpiryOf(DateTimeOffset notBefore, DateTimeOffset notAfter, DateTimeOffset now, int expiringDays)
	{
		if (now < notBefore)
		{
			return ExpiryState.NotYetValid;
		}

		if (now > notAfter)
		{
			return ExpiryState.Expired;
		}

		return notAfter - now <= TimeSpan.FromDays(Math.Max(0, expiringDays)) ? ExpiryState.Expiring : ExpiryState.Valid;
	}

	/// <summary>Whole days from <paramref name="now"/> to <paramref name="notAfter"/>, negative once it passed.</summary>
	public static int DaysLeft(DateTimeOffset notAfter, DateTimeOffset now) =>
		(int)Math.Clamp(Math.Floor((notAfter - now).TotalDays), int.MinValue, int.MaxValue);

	/// <summary>Describes every object in <paramref name="set"/>, says which keys go with which certificates, and checks a bundle's order.</summary>
	public static Inspection Inspect(MaterialSet set, DateTimeOffset now, int expiringDays)
	{
		ArgumentNullException.ThrowIfNull(set);
		List<(int Number, X509Certificate2 Certificate, CertificateInfo Info)> certificates = [];
		List<(int Number, RequestMaterial Request, RequestInfo Info)> requests = [];
		for (int index = 0; index < set.Items.Count; index++)
		{
			switch (set.Items[index])
			{
				case CertificateMaterial certificate:
					certificates.Add((index + 1, certificate.Certificate, Describe(certificate.Certificate, now, expiringDays)));
					break;
				case RequestMaterial request:
					requests.Add((index + 1, request, Describe(request)));
					break;
			}
		}

		List<InspectedItem> items = [];
		for (int index = 0; index < set.Items.Count; index++)
		{
			int number = index + 1;
			MaterialItem item = set.Items[index];
			switch (item)
			{
				case CertificateMaterial material:
				{
					CertificateInfo info = certificates.First(entry => entry.Number == number).Info;
					items.Add(new InspectedItem
					{
						Number = number,
						Kind = item.Kind,
						Format = item.Format,
						Title = info.Title,
						Subtitle = $"Issued by {info.IssuerTitle}",
						Certificate = info,
						Matches = [.. set.Items
							.Select((other, position) => (other, position))
							.Where(entry => entry.other is PrivateKeyMaterial key && KeyIdentity.Matches(key.PublicKey, material.Certificate.PublicKey))
							.Select(entry => $"Its private key is item {entry.position + 1}.")],
					});
					break;
				}

				case RequestMaterial:
				{
					RequestInfo info = requests.First(entry => entry.Number == number).Info;
					items.Add(new InspectedItem
					{
						Number = number,
						Kind = item.Kind,
						Format = item.Format,
						Title = info.Title,
						Subtitle = $"Signing request, {info.Key.Description}",
						Request = info,
					});
					break;
				}

				case PrivateKeyMaterial key:
				{
					KeyInfo info = DescribeKey(key.PublicKey);
					items.Add(new InspectedItem
					{
						Number = number,
						Kind = item.Kind,
						Format = item.Format,
						Title = $"Private key, {info.Description}",
						Subtitle = key.WasEncrypted ? "Was encrypted" : "Not encrypted",
						Key = info,
						PublicKeyPem = CertificateConverter.PublicKeyPem(key.PublicKey),
						Matches = MatchesFor(key.PublicKey, certificates, requests),
					});
					break;
				}

				case PublicKeyMaterial publicKey:
				{
					KeyInfo info = DescribeKey(publicKey.Key);
					items.Add(new InspectedItem
					{
						Number = number,
						Kind = item.Kind,
						Format = item.Format,
						Title = $"Public key, {info.Description}",
						Key = info,
						PublicKeyPem = CertificateConverter.PublicKeyPem(publicKey.Key),
						Matches = MatchesFor(publicKey.Key, certificates, requests),
					});
					break;
				}

				case LockedKeyMaterial locked:
					items.Add(new InspectedItem
					{
						Number = number,
						Kind = item.Kind,
						Format = item.Format,
						Title = locked.Format == "PFX" ? "PFX file, locked" : "Private key, encrypted",
						Subtitle = locked.Protection,
						Locked = true,
					});
					break;
			}
		}

		ChainCheck? chain = certificates.Count > 1 ? ChainCheck.Analyze([.. certificates.Select(entry => entry.Certificate)]) : null;
		return new Inspection(items, chain, set.Notes);
	}

	/// <summary>The parts of a name, most specific first: CN before O before C.</summary>
	internal static IReadOnlyList<NamePart> Parts(X500DistinguishedName name)
	{
		List<NamePart> parts = [];
		try
		{
			foreach (X500RelativeDistinguishedName part in name.EnumerateRelativeDistinguishedNames())
			{
				if (part.HasMultipleElements)
				{
					AsnWriter writer = new(AsnEncodingRules.DER);
					using (writer.PushSequence())
					{
						writer.WriteEncodedValue(part.RawData.Span);
					}

					parts.Add(new NamePart("+", new X500DistinguishedName(writer.Encode()).Name));
					continue;
				}

				string type = part.GetSingleElementType().Value ?? "";
				string label = OidNames.NameAttributes.TryGetValue(type, out string? known) ? known : type;
				parts.Add(new NamePart(label, part.GetSingleElementValue() ?? Convert.ToHexString(part.RawData.Span)));
			}
		}
		catch (CryptographicException)
		{
			parts.Clear();
			parts.Add(new NamePart("DN", name.Name));
		}

		return parts;
	}

	private static List<string> MatchesFor(
		PublicKey key,
		List<(int Number, X509Certificate2 Certificate, CertificateInfo Info)> certificates,
		List<(int Number, RequestMaterial Request, RequestInfo Info)> requests)
	{
		List<string> matches = [];
		foreach ((int number, X509Certificate2 certificate, CertificateInfo info) in certificates)
		{
			if (KeyIdentity.Matches(key, certificate.PublicKey))
			{
				matches.Add($"Matches the certificate in item {number} ({info.Title}).");
			}
		}

		foreach ((int number, RequestMaterial request, RequestInfo info) in requests)
		{
			if (KeyIdentity.Matches(key, request.Request.PublicKey))
			{
				matches.Add($"Matches the signing request in item {number} ({info.Title}).");
			}
		}

		if (matches.Count == 0 && (certificates.Count > 0 || requests.Count > 0))
		{
			matches.Add("Matches none of the certificates or requests here.");
		}

		return matches;
	}

	private static string NameText(X500DistinguishedName name) => name.Name;

	private static string TitleOf(IReadOnlyList<NamePart> parts, List<GeneralNameInfo> names, string whole) =>
		First(parts, "CN")
		?? names.FirstOrDefault(name => name.Kind == GeneralNameKind.Dns)?.Value
		?? names.FirstOrDefault()?.Value
		?? (whole.Length > 0 ? whole : "(no subject)");

	private static string? First(IReadOnlyList<NamePart> parts, string label) =>
		parts.FirstOrDefault(part => part.Label == label)?.Value;

	private static int? RsaBits(PublicKey key)
	{
		try
		{
			using RSA? rsa = key.GetRSAPublicKey();
			return rsa?.KeySize;
		}
		catch (CryptographicException)
		{
			return null;
		}
	}

	private static string? CurveOid(PublicKey key)
	{
		try
		{
			return key.EncodedParameters?.RawData is { } parameters ? new AsnReader(parameters, AsnEncodingRules.BER).ReadObjectIdentifier() : null;
		}
		catch (AsnContentException)
		{
			return null;
		}
	}

	// Fingerprints are identifiers, not signatures: SHA-1 is shown because certificate stores and older tools list it.
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "A SHA-1 fingerprint names a certificate the way older tools and stores list it; nothing is trusted on it.")]
	private static byte[] Sha1Of(byte[] der) => SHA1.HashData(der);
}
