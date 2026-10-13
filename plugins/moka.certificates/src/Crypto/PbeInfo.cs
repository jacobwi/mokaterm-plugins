using System.Formats.Asn1;
using System.Globalization;
using System.Numerics;

namespace Moka.Certificates.Crypto;

/// <summary>
/// How an encrypted key or a PFX is protected, read before any password is tried: the scheme for the user, and the
/// number of key-derivation rounds, which the file chooses and which a hostile file could set high enough to tie up
/// the machine (on the web host, the server) for minutes.
/// </summary>
internal static class PbeInfo
{
	/// <summary>The scheme and rounds of an EncryptedPrivateKeyInfo. Rounds are 0 when the scheme names none.</summary>
	public static (string Description, long Rounds) ReadEncryptedKey(ReadOnlyMemory<byte> encryptedPkcs8)
	{
		try
		{
			AsnReader outer = new(encryptedPkcs8, AsnEncodingRules.BER);
			AsnReader info = outer.ReadSequence();
			AsnReader algorithm = info.ReadSequence();
			string oid = algorithm.ReadObjectIdentifier();
			if (oid == OidNames.Pbes2)
			{
				return ReadPbes2(algorithm.ReadSequence());
			}

			if (OidNames.Pkcs12Pbe.TryGetValue(oid, out string? scheme))
			{
				AsnReader parameters = algorithm.ReadSequence();
				parameters.ReadOctetString();
				long rounds = Rounds(parameters.ReadInteger());
				return ($"{scheme}, {Count(rounds)} rounds", rounds);
			}

			return ($"Scheme {oid}", 0);
		}
		catch (AsnContentException)
		{
			return ("An encryption scheme that could not be read", 0);
		}
	}

	/// <summary>The rounds a PFX's MAC asks for, or null when it has no MAC (a PFX without a password).</summary>
	public static long? ReadPfxMacRounds(ReadOnlyMemory<byte> pfx)
	{
		try
		{
			AsnReader outer = new(pfx, AsnEncodingRules.BER);
			AsnReader body = outer.ReadSequence();
			body.ReadInteger();
			body.ReadEncodedValue();
			if (!body.HasData)
			{
				return null;
			}

			AsnReader mac = body.ReadSequence();
			mac.ReadEncodedValue();
			mac.ReadOctetString();
			return mac.HasData ? Rounds(mac.ReadInteger()) : 1;
		}
		catch (AsnContentException)
		{
			return null;
		}
	}

	public static string Count(long rounds) => rounds.ToString("N0", CultureInfo.InvariantCulture);

	private static (string Description, long Rounds) ReadPbes2(AsnReader parameters)
	{
		AsnReader kdf = parameters.ReadSequence();
		string kdfOid = kdf.ReadObjectIdentifier();
		AsnReader scheme = parameters.ReadSequence();
		string cipher = OidNames.Name(OidNames.Ciphers, scheme.ReadObjectIdentifier());
		if (kdfOid != OidNames.Pbkdf2)
		{
			return kdfOid == "1.3.6.1.4.1.11591.4.11" ? ($"{cipher}, scrypt", 0) : ($"{cipher}, key derivation {kdfOid}", 0);
		}

		AsnReader pbkdf2 = kdf.ReadSequence();
		pbkdf2.ReadEncodedValue();
		long rounds = Rounds(pbkdf2.ReadInteger());
		string prf = "HMAC-SHA1";
		if (pbkdf2.HasData && pbkdf2.PeekTag().HasSameClassAndValue(Asn1Tag.Integer))
		{
			pbkdf2.ReadInteger();
		}

		if (pbkdf2.HasData)
		{
			prf = OidNames.Name(OidNames.Prfs, pbkdf2.ReadSequence().ReadObjectIdentifier());
		}

		return ($"{cipher}, PBKDF2 with {prf}, {Count(rounds)} rounds", rounds);
	}

	private static long Rounds(BigInteger value) => value > long.MaxValue ? long.MaxValue : value < 0 ? 0 : (long)value;
}
