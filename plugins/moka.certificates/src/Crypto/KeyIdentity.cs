using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Crypto;

/// <summary>
/// Whether two public keys are the same key. The comparison is on the numbers (RSA's modulus and exponent, an EC
/// curve and point), not on the encoding, because a certificate may carry its key encoded differently from the way the
/// key file does.
/// </summary>
internal static class KeyIdentity
{
	public static bool Matches(PublicKey first, PublicKey second)
	{
		ArgumentNullException.ThrowIfNull(first);
		ArgumentNullException.ThrowIfNull(second);
		return Of(first).AsSpan().SequenceEqual(Of(second));
	}

	/// <summary>A SHA-256 over the key's numbers, or over its encoding for an algorithm this tool does not use.</summary>
	public static byte[] Of(PublicKey key)
	{
		ArgumentNullException.ThrowIfNull(key);
		try
		{
			switch (key.Oid.Value)
			{
				case OidNames.RsaKey:
				{
					using RSA? rsa = key.GetRSAPublicKey();
					if (rsa is not null)
					{
						RSAParameters parameters = rsa.ExportParameters(includePrivateParameters: false);
						return Hash("rsa", parameters.Modulus, parameters.Exponent);
					}

					break;
				}

				case OidNames.EcKey:
				{
					using ECDsa? ec = key.GetECDsaPublicKey();
					if (ec is not null)
					{
						ECParameters parameters = ec.ExportParameters(includePrivateParameters: false);
						return Hash(parameters.Curve.Oid?.Value ?? "ec", parameters.Q.X, parameters.Q.Y);
					}

					break;
				}
			}
		}
		catch (CryptographicException)
		{
			// A curve this platform cannot load still compares by its encoding below.
		}

		return Hash("spki", key.ExportSubjectPublicKeyInfo());
	}

	private static byte[] Hash(string kind, params byte[]?[] parts)
	{
		using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		hash.AppendData(System.Text.Encoding.ASCII.GetBytes(kind));
		foreach (byte[]? part in parts)
		{
			byte[] value = part ?? [];
			hash.AppendData(BitConverter.GetBytes(value.Length));
			hash.AppendData(value);
		}

		return hash.GetHashAndReset();
	}
}
