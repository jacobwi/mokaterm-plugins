using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Crypto;

public static partial class CertificateReader
{
	// The loader's own defaults refuse anything over 300,000 rounds, which turns away PFX files that follow current
	// password-hashing advice; these allow what MaxKdfRounds allows and no more.
	private static readonly Pkcs12LoaderLimits PfxLimits = new()
	{
		MacIterationLimit = (int)MaxKdfRounds,
		IndividualKdfIterationLimit = (int)MaxKdfRounds,
		TotalKdfIterationLimit = (int)(MaxKdfRounds * 4),
		MaxCertificates = MaxObjects,
		MaxKeys = 16,
	};

	/// <summary>
	/// Reads a PFX: the certificate with the key first, then its chain, each followed by its private key when it has one. The keys are copied
	/// out of whatever the platform loaded them into, so nothing stays in a key store and the set owns plain RSA and ECDSA
	/// objects.
	/// </summary>
	private static void ReadPfx(byte[] bytes, ReadState state)
	{
		Pkcs12Info info;
		try
		{
			info = Pkcs12Info.Decode(bytes, out int read, skipCopy: true);
			if (read != bytes.Length)
			{
				state.Note(NoteLevel.Error, "The PFX file has extra bytes after it.");
				return;
			}
		}
		catch (CryptographicException)
		{
			state.Note(NoteLevel.Error, "The PFX file is damaged.");
			return;
		}

		long? macRounds = PbeInfo.ReadPfxMacRounds(bytes);
		if (macRounds > MaxKdfRounds)
		{
			state.Note(NoteLevel.Error, $"The PFX asks for {PbeInfo.Count(macRounds.Value)} rounds of password hashing, more than the {PbeInfo.Count(MaxKdfRounds)} this tool allows.");
			return;
		}

		string? password = state.GivenPassword;
		switch (info.IntegrityMode)
		{
			case Pkcs12IntegrityMode.Password:
				if (!TryOpenMac(info, state.GivenPassword, out password))
				{
					string protection = macRounds is { } rounds ? $"Password, {PbeInfo.Count(rounds)} rounds" : "Password";
					state.Add(new LockedKeyMaterial("PFX", protection, passwordRefused: state.Password is not null));
					return;
				}

				break;
			case Pkcs12IntegrityMode.None:
				break;
			default:
				state.Note(NoteLevel.Error, "This PFX is sealed with a public key rather than a password, which is not supported.");
				return;
		}

		X509Certificate2Collection collection;
		try
		{
			X509KeyStorageFlags flags = OperatingSystem.IsMacOS()
				? X509KeyStorageFlags.Exportable
				: X509KeyStorageFlags.Exportable | X509KeyStorageFlags.EphemeralKeySet;
			collection = X509CertificateLoader.LoadPkcs12Collection(bytes, password, flags, PfxLimits);
		}
		catch (Pkcs12LoadLimitExceededException)
		{
			state.Note(NoteLevel.Error, "The PFX asks for more password hashing, or holds more certificates or keys, than this tool allows.");
			return;
		}
		catch (CryptographicException)
		{
			state.Note(NoteLevel.Error, "The PFX could not be opened: its contents are damaged or encrypted with a scheme this system does not support.");
			return;
		}

		try
		{
			// Platforms disagree on the order a PFX's certificates come back in, so it is set here: the certificate with
			// the key, then the chain above it, then anything else.
			X509Certificate2? leaf = collection.FirstOrDefault(certificate => certificate.HasPrivateKey) ?? (collection.Count > 0 ? collection[0] : null);
			IEnumerable<X509Certificate2> ordered = leaf is null ? [] : [leaf, .. CertificateConverter.ChainAbove(leaf, [.. collection])];
			foreach (X509Certificate2 certificate in ordered)
			{
				AddCertificate(certificate.RawData, "PFX", state);
				if (certificate.HasPrivateKey)
				{
					AddPfxKey(certificate, state);
				}
			}
		}
		finally
		{
			foreach (X509Certificate2 certificate in collection)
			{
				certificate.Dispose();
			}
		}
	}

	// Tools disagree on what "no password" means in a PFX: an empty password or none at all. Both are tried when the
	// user gave none, and the one that opens the MAC is the one the contents are read with.
	private static bool TryOpenMac(Pkcs12Info info, string? given, out string? password)
	{
		string?[] candidates = string.IsNullOrEmpty(given) ? ["", null] : [given];
		foreach (string? candidate in candidates)
		{
			try
			{
				if (info.VerifyMac(candidate))
				{
					password = candidate;
					return true;
				}
			}
			catch (CryptographicException)
			{
				// A MAC this system cannot compute counts as not opening it.
			}
		}

		password = null;
		return false;
	}

	private static void AddPfxKey(X509Certificate2 certificate, ReadState state)
	{
		using AsymmetricAlgorithm? source = (AsymmetricAlgorithm?)certificate.GetRSAPrivateKey() ?? certificate.GetECDsaPrivateKey();
		if (source is null)
		{
			state.Note(NoteLevel.Warning, Unsupported(certificate.PublicKey.Oid.Value, "private key in the PFX"));
			return;
		}

		AsymmetricAlgorithm copy = source is RSA ? RSA.Create() : ECDsa.Create();
		bool kept = false;
		try
		{
			CopyKey(source, copy);
			state.Add(new PrivateKeyMaterial(copy, "PFX", wasEncrypted: true));
			kept = true;
		}
		catch (CryptographicException)
		{
			state.Note(NoteLevel.Error, "A private key in the PFX could not be exported. It may have been made non-exportable.");
		}
		finally
		{
			if (!kept)
			{
				copy.Dispose();
			}
		}
	}

	private static void CopyKey(AsymmetricAlgorithm source, AsymmetricAlgorithm target)
	{
		byte[]? pkcs8 = null;
		try
		{
			pkcs8 = source.ExportPkcs8PrivateKey();
			target.ImportPkcs8PrivateKey(pkcs8, out _);
		}
		catch (CryptographicException)
		{
			// Windows may let a loaded key out only encrypted. A throwaway password and one round are enough for a copy
			// that never leaves this method.
			string transfer = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
			byte[] encrypted = source.ExportEncryptedPkcs8PrivateKey(transfer, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1));
			try
			{
				target.ImportEncryptedPkcs8PrivateKey(transfer, encrypted, out _);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(encrypted);
			}
		}
		finally
		{
			if (pkcs8 is not null)
			{
				CryptographicOperations.ZeroMemory(pkcs8);
			}
		}
	}
}
