using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Crypto;

/// <summary>
/// Makes keys, signing requests and certificates: self-signed ones, CAs, and certificates a loaded CA signs, from a new
/// key or from a request. Every certificate gets a random 128-bit serial, its key identifiers, and the key usage and
/// extended key usage its presets call for; a request's own extensions are never copied, only its names.
/// </summary>
public static class CertificateGenerator
{
	/// <summary>Starts a certificate a few minutes back, so a machine whose clock runs slightly behind still takes it.</summary>
	private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(5);

	public static AsymmetricAlgorithm CreateKey(KeySpec spec)
	{
		ArgumentNullException.ThrowIfNull(spec);
		return spec.Algorithm == KeyAlgorithm.Rsa
			? RSA.Create(CryptoChoices.Bits(spec.RsaSize))
			: ECDsa.Create(CryptoChoices.Curve(spec.Curve));
	}

	/// <summary>A PKCS#10 request signed with <paramref name="key"/>, asking for the spec's names and usages.</summary>
	/// <exception cref="CertificateException">The spec cannot make a request.</exception>
	public static byte[] CreateSigningRequest(AsymmetricAlgorithm key, CertificateSpec spec)
	{
		ArgumentNullException.ThrowIfNull(key);
		ArgumentNullException.ThrowIfNull(spec);
		ThrowIfProblems(spec.Problems(forRequest: true));
		CertificateRequest request = NewRequest(spec.Subject.ToName(), key, spec.Hash);
		AddUsageExtensions(request, spec, key is RSA);
		if (spec.Names.Count > 0)
		{
			request.CertificateExtensions.Add(SubjectNames.ToExtension(spec.Names, critical: spec.Subject.IsEmpty));
		}

		return request.CreateSigningRequest();
	}

	/// <summary>A certificate signed by its own key: a self-signed certificate, or a root CA when the spec says CA.</summary>
	/// <exception cref="CertificateException">The spec cannot make a certificate.</exception>
	public static X509Certificate2 CreateSelfSigned(AsymmetricAlgorithm key, CertificateSpec spec, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(key);
		ArgumentNullException.ThrowIfNull(spec);
		ThrowIfProblems(spec.Problems(forRequest: false));
		X500DistinguishedName name = spec.Subject.ToName();
		CertificateRequest request = NewRequest(name, key, spec.Hash);
		PublicKey publicKey = new(key);
		X509SubjectKeyIdentifierExtension subjectKeyId = new(publicKey, critical: false);
		AddCertificateExtensions(request, spec, key is RSA, subjectKeyId);
		request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromSubjectKeyIdentifier(subjectKeyId));
		(DateTimeOffset notBefore, DateTimeOffset notAfter) = Validity(now, spec.ValidityDays);
		return request.Create(name, SignatureGenerator(key), notBefore, notAfter, NewSerialNumber());
	}

	/// <summary>A certificate for <paramref name="subjectKey"/>, signed by the CA <paramref name="issuer"/> with <paramref name="issuerKey"/>.</summary>
	/// <exception cref="CertificateException">The spec cannot make a certificate, or the CA cannot sign it.</exception>
	public static IssuedCertificate CreateSigned(X509Certificate2 issuer, AsymmetricAlgorithm issuerKey, PublicKey subjectKey, CertificateSpec spec, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(issuer);
		ArgumentNullException.ThrowIfNull(issuerKey);
		ArgumentNullException.ThrowIfNull(subjectKey);
		ArgumentNullException.ThrowIfNull(spec);
		ThrowIfProblems(spec.Problems(forRequest: false));
		return Issue(issuer, issuerKey, subjectKey, spec.Subject.ToName(), spec, now, []);
	}

	/// <summary>
	/// A certificate for the key in <paramref name="request"/>, signed by the CA. The subject and names come from the
	/// request unless the spec gives its own; the usages, validity and hash always come from the spec.
	/// </summary>
	/// <exception cref="CertificateException">The request's signature does not hold, or the CA cannot sign it.</exception>
	public static IssuedCertificate SignRequest(X509Certificate2 issuer, AsymmetricAlgorithm issuerKey, RequestMaterial request, CertificateSpec spec, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(issuer);
		ArgumentNullException.ThrowIfNull(issuerKey);
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(spec);
		if (SignatureCheck.Verify(request.Der, request.Request.PublicKey) == false)
		{
			throw new CertificateException("The request's signature does not verify, so it was not made with the key it carries.");
		}

		List<string> notes = [];
		X500DistinguishedName subject = request.Request.SubjectName;
		if (!spec.Subject.IsEmpty)
		{
			subject = spec.Subject.ToName();
			notes.Add("The subject typed here replaced the request's.");
		}

		IReadOnlyList<GeneralNameInfo> names = spec.Names;
		if (names.Count == 0)
		{
			names = [.. CertificateReader.Describe(request).AlternativeNames
				.Where(name => name.Kind is GeneralNameKind.Dns or GeneralNameKind.IpAddress or GeneralNameKind.Email or GeneralNameKind.Uri)];
			if (names.Count > 0)
			{
				notes.Add($"The names came from the request: {string.Join(", ", names.Select(name => name.Value))}.");
			}
		}

		if (subject.EnumerateRelativeDistinguishedNames().FirstOrDefault() is null && names.Count == 0)
		{
			throw new CertificateException("The request names nobody: it has no subject and no names. Type a common name or names here.");
		}

		ThrowIfProblems(spec.LimitProblems());
		return Issue(issuer, issuerKey, request.Request.PublicKey, subject, spec with { Names = names }, now, notes);
	}

	/// <summary>16 random bytes with the top bit clear, so the serial is positive and always 128 bits long.</summary>
	public static byte[] NewSerialNumber()
	{
		byte[] serial = RandomNumberGenerator.GetBytes(16);
		serial[0] &= 0x7F;
		serial[0] |= 0x01;
		return serial;
	}

	private static IssuedCertificate Issue(
		X509Certificate2 issuer,
		AsymmetricAlgorithm issuerKey,
		PublicKey subjectKey,
		X500DistinguishedName subject,
		CertificateSpec spec,
		DateTimeOffset now,
		List<string> notes)
	{
		CheckIssuer(issuer, issuerKey, spec, now);
		if (subjectKey.Oid.Value is not (OidNames.RsaKey or OidNames.EcKey))
		{
			throw new CertificateException("Only RSA and ECDSA keys can be certified here.");
		}

		CertificateRequest request = new(subject, subjectKey, CryptoChoices.Hash(spec.Hash));
		AddCertificateExtensions(request, spec, subjectKey.Oid.Value == OidNames.RsaKey, new X509SubjectKeyIdentifierExtension(subjectKey, critical: false));
		request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromSubjectKeyIdentifier(IssuerKeyId(issuer)));

		(DateTimeOffset notBefore, DateTimeOffset notAfter) = Validity(now, spec.ValidityDays);
		DateTimeOffset issuerStart = new(issuer.NotBefore.ToUniversalTime(), TimeSpan.Zero);
		DateTimeOffset issuerEnd = new(issuer.NotAfter.ToUniversalTime(), TimeSpan.Zero);
		if (notBefore < issuerStart)
		{
			notBefore = issuerStart;
		}

		if (notAfter > issuerEnd)
		{
			notAfter = issuerEnd;
			notes.Add($"The validity was cut to end with the CA's, on {CertificateReport.Date(issuerEnd)}.");
		}

		X509Certificate2 certificate = request.Create(issuer.SubjectName, SignatureGenerator(issuerKey), notBefore, notAfter, NewSerialNumber());
		return new IssuedCertificate(certificate, notes);
	}

	private static void CheckIssuer(X509Certificate2 issuer, AsymmetricAlgorithm issuerKey, CertificateSpec spec, DateTimeOffset now)
	{
		if (!KeyIdentity.Matches(issuer.PublicKey, new PublicKey(issuerKey)))
		{
			throw new CertificateException("The private key does not belong to the CA certificate.");
		}

		X509BasicConstraintsExtension? constraints = issuer.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
		if (constraints is not { CertificateAuthority: true })
		{
			throw new CertificateException("The CA certificate is not a CA: its basic constraints do not say CA:TRUE, so what it signs would not be trusted.");
		}

		if (spec.IsCertificateAuthority && constraints is { HasPathLengthConstraint: true, PathLengthConstraint: 0 })
		{
			throw new CertificateException("This CA allows no CAs below it (its path length is 0).");
		}

		if (issuer.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault() is { } usage && !usage.KeyUsages.HasFlag(X509KeyUsageFlags.KeyCertSign))
		{
			throw new CertificateException("The CA certificate's key usage does not include certificate signing.");
		}

		if (new DateTimeOffset(issuer.NotAfter.ToUniversalTime(), TimeSpan.Zero) <= now)
		{
			throw new CertificateException("The CA certificate has expired.");
		}
	}

	private static ReadOnlySpan<byte> IssuerKeyId(X509Certificate2 issuer) =>
		issuer.Extensions.OfType<X509SubjectKeyIdentifierExtension>().FirstOrDefault() is { } existing
			? existing.SubjectKeyIdentifierBytes.Span
			: new X509SubjectKeyIdentifierExtension(issuer.PublicKey, critical: false).SubjectKeyIdentifierBytes.Span;

	private static CertificateRequest NewRequest(X500DistinguishedName name, AsymmetricAlgorithm key, SignatureHash hash) => key switch
	{
		RSA rsa => new CertificateRequest(name, rsa, CryptoChoices.Hash(hash), RSASignaturePadding.Pkcs1),
		ECDsa ecdsa => new CertificateRequest(name, ecdsa, CryptoChoices.Hash(hash)),
		_ => throw new CertificateException("Only RSA and ECDSA keys can be used here."),
	};

	private static X509SignatureGenerator SignatureGenerator(AsymmetricAlgorithm key) => key switch
	{
		RSA rsa => X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1),
		ECDsa ecdsa => X509SignatureGenerator.CreateForECDsa(ecdsa),
		_ => throw new CertificateException("Only RSA and ECDSA keys can sign here."),
	};

	private static void AddCertificateExtensions(CertificateRequest request, CertificateSpec spec, bool rsa, X509SubjectKeyIdentifierExtension subjectKeyId)
	{
		bool ca = spec.IsCertificateAuthority;
		request.CertificateExtensions.Add(new X509BasicConstraintsExtension(ca, ca && spec.PathLength is not null, spec.PathLength ?? 0, critical: true));
		AddUsageExtensions(request, spec, rsa, basicConstraints: false);
		if (spec.Names.Count > 0)
		{
			request.CertificateExtensions.Add(SubjectNames.ToExtension(spec.Names, critical: spec.Subject.IsEmpty));
		}

		request.CertificateExtensions.Add(subjectKeyId);
	}

	private static void AddUsageExtensions(CertificateRequest request, CertificateSpec spec, bool rsa, bool basicConstraints = true)
	{
		if (basicConstraints && spec.IsCertificateAuthority)
		{
			request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, spec.PathLength is not null, spec.PathLength ?? 0, critical: true));
		}

		X509KeyUsageFlags usage = KeyUsage(spec.Usages, rsa);
		if (usage != X509KeyUsageFlags.None)
		{
			request.CertificateExtensions.Add(new X509KeyUsageExtension(usage, critical: true));
		}

		OidCollection purposes = Purposes(spec.Usages);
		if (purposes.Count > 0)
		{
			request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(purposes, critical: false));
		}
	}

	/// <summary>The key usage the presets call for. RSA encrypts keys in TLS 1.2 and S/MIME; EC agrees on them instead.</summary>
	internal static X509KeyUsageFlags KeyUsage(UsagePresets presets, bool rsa)
	{
		X509KeyUsageFlags usage = X509KeyUsageFlags.None;
		if (presets.HasFlag(UsagePresets.CertificateAuthority))
		{
			usage |= X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature;
		}

		if (presets.HasFlag(UsagePresets.TlsServer))
		{
			usage |= X509KeyUsageFlags.DigitalSignature | (rsa ? X509KeyUsageFlags.KeyEncipherment : X509KeyUsageFlags.None);
		}

		if (presets.HasFlag(UsagePresets.TlsClient) || presets.HasFlag(UsagePresets.CodeSigning))
		{
			usage |= X509KeyUsageFlags.DigitalSignature;
		}

		if (presets.HasFlag(UsagePresets.Email))
		{
			usage |= X509KeyUsageFlags.DigitalSignature | (rsa ? X509KeyUsageFlags.KeyEncipherment : X509KeyUsageFlags.KeyAgreement);
		}

		return usage;
	}

	internal static OidCollection Purposes(UsagePresets presets)
	{
		OidCollection purposes = [];
		if (presets.HasFlag(UsagePresets.TlsServer))
		{
			purposes.Add(new Oid(OidNames.ServerAuth));
		}

		if (presets.HasFlag(UsagePresets.TlsClient))
		{
			purposes.Add(new Oid(OidNames.ClientAuth));
		}

		if (presets.HasFlag(UsagePresets.CodeSigning))
		{
			purposes.Add(new Oid(OidNames.CodeSigning));
		}

		if (presets.HasFlag(UsagePresets.Email))
		{
			purposes.Add(new Oid(OidNames.EmailProtection));
		}

		return purposes;
	}

	private static (DateTimeOffset NotBefore, DateTimeOffset NotAfter) Validity(DateTimeOffset now, int days)
	{
		DateTimeOffset start = now.ToUniversalTime() - ClockSkew;
		start = new DateTimeOffset(start.Ticks - (start.Ticks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
		return (start, start.AddDays(days));
	}

	private static void ThrowIfProblems(IReadOnlyList<string> problems)
	{
		if (problems.Count > 0)
		{
			throw new CertificateException(problems[0]);
		}
	}
}
