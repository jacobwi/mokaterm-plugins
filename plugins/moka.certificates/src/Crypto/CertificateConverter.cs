using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Crypto;

/// <summary>
/// Turns what the reader read into other forms: PEM and DER, PFX and PEM, PKCS#8 and the algorithm's own key format,
/// encrypted keys and plain ones. Private keys come out of here only as PEM strings for the views to copy as secrets
/// or save; the bytes in between are cleared.
/// </summary>
public static class CertificateConverter
{
	/// <summary>PBKDF2 rounds for a key this tool encrypts: well above OpenSSL's 2,048, and quick enough to open anywhere.</summary>
	public const int KeyEncryptionRounds = 100_000;

	/// <summary>Rounds for an AES PFX. Windows reads these without trouble.</summary>
	public const int ModernPfxRounds = 100_000;

	/// <summary>What Windows itself used for 3DES PFX files, which the old readers expect.</summary>
	public const int LegacyPfxRounds = 2_000;

	public static string ToPem(string label, ReadOnlySpan<byte> der) => new string(PemEncoding.Write(label, der)) + "\n";

	public static string CertificatePem(X509Certificate2 certificate)
	{
		ArgumentNullException.ThrowIfNull(certificate);
		return certificate.ExportCertificatePem() + "\n";
	}

	public static string CertificatesPem(IEnumerable<X509Certificate2> certificates)
	{
		ArgumentNullException.ThrowIfNull(certificates);
		return string.Concat(certificates.Select(CertificatePem));
	}

	public static string RequestPem(ReadOnlySpan<byte> der) => ToPem("CERTIFICATE REQUEST", der);

	public static string PublicKeyPem(PublicKey key)
	{
		ArgumentNullException.ThrowIfNull(key);
		return ToPem("PUBLIC KEY", key.ExportSubjectPublicKeyInfo());
	}

	/// <summary>The public key a described certificate carries, as PEM.</summary>
	public static string PublicKeyPemOf(CertificateInfo info)
	{
		ArgumentNullException.ThrowIfNull(info);
		using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(info.Der);
		return PublicKeyPem(certificate.PublicKey);
	}

	/// <summary>A private key as PEM, unencrypted. The result is a secret.</summary>
	public static string PrivateKeyPem(AsymmetricAlgorithm key, PrivateKeyFormat format)
	{
		ArgumentNullException.ThrowIfNull(key);
		if (format == PrivateKeyFormat.Pkcs8)
		{
			return key.ExportPkcs8PrivateKeyPem() + "\n";
		}

		return key switch
		{
			RSA rsa => rsa.ExportRSAPrivateKeyPem() + "\n",
			ECDsa ecdsa => ecdsa.ExportECPrivateKeyPem() + "\n",
			_ => throw new CertificateException("Only RSA and EC keys have a traditional form."),
		};
	}

	/// <summary>A private key as encrypted PKCS#8 PEM: AES-256-CBC, PBKDF2 with HMAC-SHA256.</summary>
	/// <exception cref="CertificateException">The password is empty.</exception>
	public static string EncryptedPrivateKeyPem(AsymmetricAlgorithm key, string password)
	{
		ArgumentNullException.ThrowIfNull(key);
		if (string.IsNullOrEmpty(password))
		{
			throw new CertificateException("Type a password to encrypt the key with.");
		}

		PbeParameters parameters = new(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, KeyEncryptionRounds);
		return key.ExportEncryptedPkcs8PrivateKeyPem(password, parameters) + "\n";
	}

	/// <summary>Each PEM block as DER, in order.</summary>
	/// <exception cref="CertificateException">There is no PEM block, or one is damaged.</exception>
	public static IReadOnlyList<DerBlock> PemToDer(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		if (!PemBlocks.HasBlocks(text))
		{
			throw new CertificateException("There is no PEM block here: PEM starts with a -----BEGIN line.");
		}

		List<DerBlock> blocks = [];
		foreach (PemBlock block in PemBlocks.Find(text, CertificateReader.MaxObjects))
		{
			if (block.Problem is { } problem)
			{
				throw new CertificateException(problem);
			}

			if (!PemBlocks.TryDecode(text, block, out byte[]? der, out string? decodeProblem))
			{
				throw new CertificateException(decodeProblem);
			}

			blocks.Add(new DerBlock(block.Label, der, ExtensionFor(block.Label, der)));
		}

		return blocks;
	}

	/// <summary>One DER structure as PEM, under the label its type is written with.</summary>
	/// <exception cref="CertificateException">It is a PFX, or not a type this tool knows.</exception>
	public static string DerToPem(byte[] der)
	{
		ArgumentNullException.ThrowIfNull(der);
		DerType type = DerDetector.Identify(der);
		if (type == DerType.Pfx)
		{
			throw new CertificateException("A PFX holds several things and has no single PEM form. Use PFX to PEM.");
		}

		return DerDetector.PemLabel(type) is { } label
			? ToPem(label, der)
			: throw new CertificateException("This is not a certificate, key or signing request this tool knows, so it has no PEM label.");
	}

	/// <summary>
	/// A PFX as PEM: the certificate the key belongs to, the key (encrypted when <paramref name="keyPassword"/> is given),
	/// and the other certificates ordered from that certificate's issuer up.
	/// </summary>
	public static PemBundle PfxToPem(MaterialSet set, PrivateKeyFormat format, string? keyPassword)
	{
		ArgumentNullException.ThrowIfNull(set);
		if (set.NeedsPassword)
		{
			throw new CertificateException(set.PasswordRefused ? "That password does not open the PFX." : "The PFX is protected: enter its password.");
		}

		List<string> notes = [];
		IReadOnlyList<PrivateKeyMaterial> keys = set.PrivateKeys;
		PrivateKeyMaterial? key = keys.Count > 0 ? keys[0] : null;
		if (keys.Count > 1)
		{
			notes.Add($"The PFX holds {keys.Count} private keys; only the first is written.");
		}

		X509Certificate2? leaf = set.CertificateFor(key);
		if (leaf is null && key is null)
		{
			throw new CertificateException("The PFX holds no certificate and no key.");
		}

		IReadOnlyList<X509Certificate2> chain = leaf is null ? [] : ChainAbove(leaf, set.Certificates);
		string? keyPem = key is null
			? null
			: string.IsNullOrEmpty(keyPassword)
				? PrivateKeyPem(key.Key, format)
				: EncryptedPrivateKeyPem(key.Key, keyPassword);
		if (key is null)
		{
			notes.Add("The PFX holds no private key.");
		}
		else if (!string.IsNullOrEmpty(keyPassword) && format == PrivateKeyFormat.Traditional)
		{
			notes.Add("An encrypted key is always written as PKCS#8.");
		}

		return new PemBundle
		{
			Certificate = leaf is null ? null : CertificatePem(leaf),
			PrivateKey = keyPem,
			Chain = chain.Count > 0 ? CertificatesPem(chain) : null,
			Notes = notes,
		};
	}

	/// <summary>
	/// A PFX of the certificates in <paramref name="set"/> and its private key, if it has one: the certificate the key
	/// belongs to first, then its chain. The key and the certificates are encrypted under the password.
	/// </summary>
	/// <exception cref="CertificateException">There is nothing to put in it, or the key belongs to none of the certificates.</exception>
	public static byte[] ToPfx(MaterialSet set, PfxOptions options)
	{
		ArgumentNullException.ThrowIfNull(set);
		ArgumentNullException.ThrowIfNull(options);
		if (set.NeedsPassword)
		{
			throw new CertificateException(set.PasswordRefused ? "That password does not open the private key." : "The private key is encrypted: enter its password above.");
		}

		if (set.Certificates.Count == 0)
		{
			throw new CertificateException("A PFX needs at least one certificate.");
		}

		IReadOnlyList<PrivateKeyMaterial> keys = set.PrivateKeys;
		if (keys.Count > 1)
		{
			throw new CertificateException("More than one private key was found. Keep only the one that goes with the certificate.");
		}

		PrivateKeyMaterial? key = keys.Count > 0 ? keys[0] : null;
		X509Certificate2 leaf = set.CertificateFor(key)!;
		if (key is not null && !KeyIdentity.Matches(leaf.PublicKey, key.PublicKey))
		{
			throw new CertificateException("The private key belongs to none of the certificates.");
		}

		return ToPfx(leaf, key?.Key, ChainAbove(leaf, set.Certificates), options);
	}

	/// <summary>A PFX of <paramref name="leaf"/>, its key (if any) and <paramref name="chain"/>, in that order.</summary>
	/// <exception cref="CertificateException">The key does not belong to the certificate.</exception>
	public static byte[] ToPfx(X509Certificate2 leaf, AsymmetricAlgorithm? key, IReadOnlyList<X509Certificate2> chain, PfxOptions options)
	{
		ArgumentNullException.ThrowIfNull(leaf);
		ArgumentNullException.ThrowIfNull(chain);
		ArgumentNullException.ThrowIfNull(options);
		if (key is not null && !KeyIdentity.Matches(leaf.PublicKey, new PublicKey(key)))
		{
			throw new CertificateException("The private key does not belong to the certificate.");
		}

		List<X509Certificate2> ordered = [leaf, .. chain.Where(certificate => !certificate.RawData.AsSpan().SequenceEqual(leaf.RawData))];
		bool legacy = options.Encryption == PfxEncryption.Legacy3Des;
		PbeParameters parameters = legacy
			? new PbeParameters(PbeEncryptionAlgorithm.TripleDes3KeyPkcs12, LegacyHash(), LegacyPfxRounds)
			: new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, ModernPfxRounds);
		byte[] localKeyId = leaf.GetCertHash(HashAlgorithmName.SHA256);
		string friendlyName = options.FriendlyName.Trim();

		Pkcs12SafeContents certificates = new();
		foreach (X509Certificate2 certificate in ordered)
		{
			Pkcs12CertBag bag = certificates.AddCertificate(certificate);
			if (ReferenceEquals(certificate, leaf))
			{
				Label(bag, key is null ? null : localKeyId, friendlyName);
			}
		}

		Pkcs12Builder builder = new();
		builder.AddSafeContentsEncrypted(certificates, options.Password, parameters);
		if (key is not null)
		{
			Pkcs12SafeContents keyContents = new();
			Label(keyContents.AddShroudedKey(key, options.Password, parameters), localKeyId, friendlyName);
			builder.AddSafeContentsUnencrypted(keyContents);
		}

		builder.SealWithMac(options.Password, legacy ? LegacyHash() : HashAlgorithmName.SHA256, legacy ? LegacyPfxRounds : ModernPfxRounds);
		return builder.Encode();
	}

	/// <summary>The certificates above <paramref name="leaf"/>, each the issuer of the one before, then any left over.</summary>
	public static IReadOnlyList<X509Certificate2> ChainAbove(X509Certificate2 leaf, IReadOnlyList<X509Certificate2> certificates)
	{
		ArgumentNullException.ThrowIfNull(leaf);
		ArgumentNullException.ThrowIfNull(certificates);
		List<X509Certificate2> remaining = [.. certificates.Where(certificate => !certificate.RawData.AsSpan().SequenceEqual(leaf.RawData))];
		List<X509Certificate2> chain = [];
		X509Certificate2 current = leaf;
		while (!current.SubjectName.RawData.AsSpan().SequenceEqual(current.IssuerName.RawData)
			&& remaining.FirstOrDefault(candidate => candidate.SubjectName.RawData.AsSpan().SequenceEqual(current.IssuerName.RawData)) is { } issuer)
		{
			chain.Add(issuer);
			remaining.Remove(issuer);
			current = issuer;
		}

		chain.AddRange(remaining);
		return chain;
	}

	private static void Label(Pkcs12SafeBag bag, byte[]? localKeyId, string friendlyName)
	{
		if (localKeyId is not null)
		{
			bag.Attributes.Add(new Pkcs9LocalKeyId(localKeyId));
		}

		if (friendlyName.Length > 0)
		{
			AsnWriter writer = new(AsnEncodingRules.DER);
			writer.WriteCharacterString(UniversalTagNumber.BMPString, friendlyName);
			bag.Attributes.Add(new Pkcs9AttributeObject(new Oid(OidNames.FriendlyName), writer.Encode()));
		}
	}

	private static string ExtensionFor(string label, byte[] der) => label switch
	{
		"CERTIFICATE" or "TRUSTED CERTIFICATE" => ".cer",
		"CERTIFICATE REQUEST" or "NEW CERTIFICATE REQUEST" => ".csr",
		"PUBLIC KEY" or "RSA PUBLIC KEY" => ".pub.der",
		"PKCS7" or "CMS" => ".p7b",
		_ when label.Contains("PRIVATE KEY", StringComparison.Ordinal) => ".key.der",
		_ => DerDetector.FileExtension(DerDetector.Identify(der)),
	};

	// The legacy PFX is SHA-1 throughout because that is all its readers know; nothing new relies on it.
	private static HashAlgorithmName LegacyHash() => HashAlgorithmName.SHA1;
}
