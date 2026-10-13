using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Crypto;

/// <summary>
/// The one reader of certificates and keys: every view hands it what the user pasted or opened (PEM bundles, DER, PFX,
/// signing requests, private and public keys, encrypted ones with their password) and gets back the objects in order,
/// plus a note for anything it could not read. The same class describes what it read (see the Describe partial), so a
/// certificate reads the same in the tool, in a server's chain and on the System page.
/// </summary>
public static partial class CertificateReader
{
	/// <summary>The most input read at once: far more than any bundle, and small enough that a stray huge paste is refused rather than parsed.</summary>
	public const int MaxInputBytes = 8 * 1024 * 1024;

	/// <summary>The most objects kept from one input.</summary>
	public const int MaxObjects = 256;

	/// <summary>The most key-derivation rounds a password may cost. OpenSSL writes 2,048 and Windows 2,000; 600,000 is a cautious modern choice.</summary>
	public const long MaxKdfRounds = 2_000_000;

	/// <summary>Reads everything in <paramref name="input"/>. Encrypted keys and PFX files open with <paramref name="password"/>.</summary>
	public static MaterialSet Read(CryptoInput input, string? password = null)
	{
		ArgumentNullException.ThrowIfNull(input);
		ReadState state = new(password);
		if (input.Bytes is { } bytes)
		{
			if (bytes.Length > MaxInputBytes)
			{
				state.Note(NoteLevel.Error, "That file is too large to be a certificate or a key.");
			}
			else
			{
				ReadBinary(bytes, "DER", state);
			}
		}
		else if (!string.IsNullOrWhiteSpace(input.Text))
		{
			ReadText(input.Text, state);
		}

		return state.ToSet();
	}

	/// <summary>Reads pasted text: PEM blocks wherever they are, or base64 without armor.</summary>
	private static void ReadText(string text, ReadState state)
	{
		if (text.Length > MaxInputBytes)
		{
			state.Note(NoteLevel.Error, "That is too much text to be a certificate or a key.");
			return;
		}

		if (!PemBlocks.HasBlocks(text))
		{
			if (BinaryText.TryParseBase64(text, out byte[]? decoded))
			{
				ReadBinary(decoded, "Base64 DER", state);
				CryptographicOperations.ZeroMemory(decoded);
				return;
			}

			state.Note(NoteLevel.Error, "Nothing here looks like a certificate or a key. Paste PEM (-----BEGIN ...), base64, or open a file.");
			return;
		}

		foreach (PemBlock block in PemBlocks.Find(text, MaxObjects + 1))
		{
			if (block.Problem is { } problem)
			{
				state.Note(NoteLevel.Error, problem);
				break;
			}

			if (state.Full)
			{
				state.NoteFull();
				break;
			}

			ReadPemBlock(text, block, state);
		}
	}

	private static void ReadPemBlock(string text, PemBlock block, ReadState state)
	{
		if (!PemBlocks.TryDecode(text, block, out byte[]? der, out string? problem))
		{
			state.Note(NoteLevel.Error, problem);
			return;
		}

		string label = block.Label;
		try
		{
			switch (label)
			{
				case "CERTIFICATE" or "X509 CERTIFICATE" or "X.509 CERTIFICATE":
					AddCertificate(der, "PEM", state);
					break;
				case "TRUSTED CERTIFICATE":
					AddTrustedCertificate(der, state);
					break;
				case "CERTIFICATE REQUEST" or "NEW CERTIFICATE REQUEST":
					AddRequest(der, "PEM", state);
					break;
				case "PRIVATE KEY":
					AddPkcs8(der, "PEM, PKCS#8", wasEncrypted: false, state);
					break;
				case "ENCRYPTED PRIVATE KEY":
					AddEncryptedPkcs8(der, "PEM, encrypted PKCS#8", state);
					break;
				case "RSA PRIVATE KEY":
					AddRsaPrivateKey(der, "PEM, PKCS#1", state);
					break;
				case "EC PRIVATE KEY":
					AddEcPrivateKey(der, "PEM, SEC1", state);
					break;
				case "PUBLIC KEY":
					AddPublicKey(der, "PEM", state);
					break;
				case "RSA PUBLIC KEY":
					AddRsaPublicKey(der, "PEM, PKCS#1", state);
					break;
				case "PKCS7" or "PKCS #7 SIGNED DATA" or "CMS":
					AddPkcs7(der, "PEM, PKCS#7", state);
					break;
				case "EC PARAMETERS":
					// openssl ecparam writes the curve ahead of the key; the key carries it too.
					break;
				case "OPENSSH PRIVATE KEY":
					state.Note(NoteLevel.Warning, "OpenSSH private keys are not read here. Mokaterm's keychain opens them; ssh-keygen -p -m PKCS8 -f key turns one into PKCS#8.");
					break;
				case "X509 CRL":
					state.Note(NoteLevel.Info, "A revocation list (CRL) was skipped: this version does not read CRLs.");
					break;
				case "DSA PRIVATE KEY":
					state.Note(NoteLevel.Warning, "DSA keys are not supported.");
					break;
				default:
					state.Note(NoteLevel.Warning, $"A {Shown(label)} block was skipped: that is not a type this tool reads.");
					break;
			}
		}
		finally
		{
			CryptographicOperations.ZeroMemory(der);
		}
	}

	/// <summary>Reads one DER structure of any type the detector knows.</summary>
	private static void ReadBinary(byte[] bytes, string format, ReadState state)
	{
		switch (DerDetector.Identify(bytes))
		{
			case DerType.Pfx:
				ReadPfx(bytes, state);
				break;
			case DerType.Certificate:
				AddCertificate(bytes, format, state);
				break;
			case DerType.CertificateRequest:
				AddRequest(bytes, format, state);
				break;
			case DerType.PrivateKey:
				AddPkcs8(bytes, format + ", PKCS#8", wasEncrypted: false, state);
				break;
			case DerType.EncryptedPrivateKey:
				AddEncryptedPkcs8(bytes, format + ", encrypted PKCS#8", state);
				break;
			case DerType.RsaPrivateKey:
				AddRsaPrivateKey(bytes, format + ", PKCS#1", state);
				break;
			case DerType.EcPrivateKey:
				AddEcPrivateKey(bytes, format + ", SEC1", state);
				break;
			case DerType.PublicKey:
				AddPublicKey(bytes, format, state);
				break;
			case DerType.RsaPublicKey:
				AddRsaPublicKey(bytes, format + ", PKCS#1", state);
				break;
			case DerType.Pkcs7:
				AddPkcs7(bytes, "PKCS#7", state);
				break;
			default:
				state.Note(NoteLevel.Error, "This is not a certificate, key, signing request or PFX file that this tool reads.");
				break;
		}
	}

	private static void AddCertificate(byte[] der, string format, ReadState state)
	{
		try
		{
			state.Add(new CertificateMaterial(X509CertificateLoader.LoadCertificate(der), format));
		}
		catch (CryptographicException)
		{
			state.Note(NoteLevel.Error, "A certificate could not be read: it is damaged or not X.509.");
		}
	}

	// OpenSSL's TRUSTED CERTIFICATE is the certificate followed by its own trust settings; only the certificate is kept.
	private static void AddTrustedCertificate(byte[] der, ReadState state)
	{
		try
		{
			System.Formats.Asn1.AsnReader reader = new(der, System.Formats.Asn1.AsnEncodingRules.BER);
			AddCertificate(reader.ReadEncodedValue().ToArray(), "PEM, trusted", state);
		}
		catch (System.Formats.Asn1.AsnContentException)
		{
			state.Note(NoteLevel.Error, "A trusted certificate block could not be read.");
		}
	}

	private static void AddRequest(byte[] der, string format, ReadState state)
	{
		try
		{
			CertificateRequest request = CertificateRequest.LoadSigningRequest(
				der,
				HashAlgorithmName.SHA256,
				out int read,
				CertificateRequestLoadOptions.SkipSignatureValidation | CertificateRequestLoadOptions.UnsafeLoadCertificateExtensions);
			if (read != der.Length)
			{
				state.Note(NoteLevel.Error, "A signing request has extra bytes after it.");
				return;
			}

			state.Add(new RequestMaterial(request, [.. der], format));
		}
		catch (CryptographicException)
		{
			state.Note(NoteLevel.Error, "A signing request could not be read.");
		}
	}

	private static void AddPkcs8(byte[] der, string format, bool wasEncrypted, ReadState state)
	{
		string? algorithm;
		try
		{
			algorithm = Pkcs8PrivateKeyInfo.Decode(der, out _, skipCopy: true).AlgorithmId.Value;
		}
		catch (CryptographicException)
		{
			state.Note(NoteLevel.Error, "A private key could not be read.");
			return;
		}

		AsymmetricAlgorithm? key = null;
		try
		{
			switch (algorithm)
			{
				case OidNames.RsaKey:
				{
					RSA rsa = RSA.Create();
					key = rsa;
					rsa.ImportPkcs8PrivateKey(der, out _);
					break;
				}

				case OidNames.EcKey:
				{
					ECDsa ecdsa = ECDsa.Create();
					key = ecdsa;
					ecdsa.ImportPkcs8PrivateKey(der, out _);
					break;
				}

				default:
					state.Note(NoteLevel.Warning, Unsupported(algorithm, "private key"));
					return;
			}

			state.Add(new PrivateKeyMaterial(key, format, wasEncrypted));
			key = null;
		}
		catch (CryptographicException)
		{
			state.Note(NoteLevel.Error, "A private key could not be read: it is damaged or uses a curve this system does not support.");
		}
		finally
		{
			key?.Dispose();
		}
	}

	private static void AddEncryptedPkcs8(byte[] der, string format, ReadState state)
	{
		(string protection, long rounds) = PbeInfo.ReadEncryptedKey(der);
		if (rounds > MaxKdfRounds)
		{
			state.Note(NoteLevel.Error, $"The encrypted private key asks for {PbeInfo.Count(rounds)} rounds of password hashing, more than the {PbeInfo.Count(MaxKdfRounds)} this tool allows.");
			return;
		}

		if (state.Password is not { } password)
		{
			state.Add(new LockedKeyMaterial(format, protection, passwordRefused: false));
			return;
		}

		byte[]? plain = null;
		try
		{
			Pkcs8PrivateKeyInfo info = Pkcs8PrivateKeyInfo.DecryptAndDecode(password, der, out _);
			plain = info.Encode();
		}
		catch (CryptographicException)
		{
			state.Add(new LockedKeyMaterial(format, protection, passwordRefused: true));
			return;
		}

		try
		{
			AddPkcs8(plain, format, wasEncrypted: true, state);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(plain);
		}
	}

	private static void AddRsaPrivateKey(byte[] der, string format, ReadState state)
	{
		RSA rsa = RSA.Create();
		bool kept = false;
		try
		{
			rsa.ImportRSAPrivateKey(der, out int read);
			if (read != der.Length)
			{
				throw new CryptographicException();
			}

			state.Add(new PrivateKeyMaterial(rsa, format, wasEncrypted: false));
			kept = true;
		}
		catch (CryptographicException)
		{
			state.Note(NoteLevel.Error, "An RSA private key could not be read.");
		}
		finally
		{
			if (!kept)
			{
				rsa.Dispose();
			}
		}
	}

	private static void AddEcPrivateKey(byte[] der, string format, ReadState state)
	{
		ECDsa ecdsa = ECDsa.Create();
		bool kept = false;
		try
		{
			ecdsa.ImportECPrivateKey(der, out int read);
			if (read != der.Length)
			{
				throw new CryptographicException();
			}

			state.Add(new PrivateKeyMaterial(ecdsa, format, wasEncrypted: false));
			kept = true;
		}
		catch (CryptographicException)
		{
			state.Note(NoteLevel.Error, "An EC private key could not be read: it is damaged or uses a curve this system does not support.");
		}
		finally
		{
			if (!kept)
			{
				ecdsa.Dispose();
			}
		}
	}

	private static void AddPublicKey(byte[] der, string format, ReadState state)
	{
		try
		{
			PublicKey key = PublicKey.CreateFromSubjectPublicKeyInfo(der, out int read);
			if (read != der.Length)
			{
				throw new CryptographicException();
			}

			state.Add(new PublicKeyMaterial(key, format));
		}
		catch (CryptographicException)
		{
			state.Note(NoteLevel.Error, "A public key could not be read.");
		}
	}

	private static void AddRsaPublicKey(byte[] der, string format, ReadState state)
	{
		try
		{
			using RSA rsa = RSA.Create();
			rsa.ImportRSAPublicKey(der, out int read);
			if (read != der.Length)
			{
				throw new CryptographicException();
			}

			state.Add(new PublicKeyMaterial(new PublicKey(rsa), format));
		}
		catch (CryptographicException)
		{
			state.Note(NoteLevel.Error, "An RSA public key could not be read.");
		}
	}

	private static void AddPkcs7(byte[] der, string format, ReadState state)
	{
		SignedCms cms = new();
		try
		{
			cms.Decode(der);
		}
		catch (CryptographicException)
		{
			state.Note(NoteLevel.Error, "A PKCS#7 bundle could not be read.");
			return;
		}

		if (cms.Certificates.Count == 0)
		{
			state.Note(NoteLevel.Info, "A PKCS#7 structure holds no certificates. To check a signature, use Sign and verify.");
			return;
		}

		foreach (X509Certificate2 certificate in cms.Certificates)
		{
			using (certificate)
			{
				AddCertificate(certificate.RawData, format, state);
			}
		}
	}

	private static string Unsupported(string? algorithm, string what) => algorithm switch
	{
		OidNames.Ed25519 => $"An Ed25519 {what} was found. Ed25519 is not supported in this version.",
		OidNames.Ed448 => $"An Ed448 {what} was found. Ed448 is not supported.",
		OidNames.X25519 or OidNames.X448 => $"An X25519 or X448 {what} was found. Key agreement keys are not supported.",
		OidNames.RsaPssKey => $"An RSA-PSS restricted {what} was found. Those are not supported; a plain RSA key works.",
		OidNames.DsaKey => $"A DSA {what} was found. DSA is not supported.",
		_ => $"A {what} of an algorithm this tool does not read ({algorithm}) was skipped.",
	};

	private static string Shown(string label) => PemBlocks.Shown(label);
}
