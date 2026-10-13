using System.Globalization;
using System.Security.Cryptography;

namespace Moka.DevTools.Jwt;

/// <summary>
/// Checks a token's signature with the algorithm its header names: HS256/384/512 against a shared secret, RS and PS
/// against an RSA key, ES against an EC key on the matching curve. It says whether the signature holds, never whether
/// the token should be trusted (its times, issuer and audience are the view's to show).
/// </summary>
public static class JwtVerifier
{
	public static JwtFamily FamilyOf(string? algorithm) => algorithm switch
	{
		"none" => JwtFamily.None,
		"HS256" or "HS384" or "HS512" => JwtFamily.Hmac,
		"RS256" or "RS384" or "RS512" or "PS256" or "PS384" or "PS512" or "ES256" or "ES384" or "ES512" => JwtFamily.PublicKey,
		_ => JwtFamily.Unsupported,
	};

	/// <summary>Checks an HS token. The caller owns <paramref name="secret"/> and clears it afterwards.</summary>
	public static JwtVerification VerifyHmac(JwtToken token, ReadOnlySpan<byte> secret)
	{
		ArgumentNullException.ThrowIfNull(token);
		if (Unchecked(token, JwtFamily.Hmac) is { } refusal)
		{
			return refusal;
		}

		if (secret.IsEmpty)
		{
			return JwtVerification.NotChecked("Type the secret to check with.");
		}

		HashAlgorithmName hash = HashOf(token.Algorithm!);
		byte[] mac = hash.Name switch
		{
			"SHA256" => HMACSHA256.HashData(secret, token.SigningInput),
			"SHA384" => HMACSHA384.HashData(secret, token.SigningInput),
			_ => HMACSHA512.HashData(secret, token.SigningInput),
		};

		List<string> notes = [];
		if (secret.Length < mac.Length)
		{
			notes.Add(string.Create(CultureInfo.InvariantCulture,
				$"The secret is {secret.Length} bytes; RFC 7518 asks for at least {mac.Length} for {token.Algorithm}, so it is easier to guess than it should be."));
		}

		return CryptographicOperations.FixedTimeEquals(mac, token.Signature)
			? new JwtVerification(JwtVerdict.Verified, $"The signature matches: this token was signed with this secret, using {token.Algorithm}.", notes)
			: new JwtVerification(JwtVerdict.Failed, "The signature does not match this secret. The token was changed, or signed with another secret.", notes);
	}

	public static JwtVerification VerifyPublicKey(JwtToken token, JwtPublicKey key)
	{
		ArgumentNullException.ThrowIfNull(token);
		ArgumentNullException.ThrowIfNull(key);
		if (Unchecked(token, JwtFamily.PublicKey) is { } refusal)
		{
			return refusal;
		}

		string algorithm = token.Algorithm!;
		HashAlgorithmName hash = HashOf(algorithm);
		List<string> notes = [.. key.Notes];
		bool valid;
		switch (algorithm[0], key.Key)
		{
			case ('R' or 'P', RSA rsa):
				if (rsa.KeySize < 2048)
				{
					notes.Add(string.Create(CultureInfo.InvariantCulture, $"The key is RSA {rsa.KeySize}; 2048 bits is the least RFC 7518 allows."));
				}

				valid = rsa.VerifyData(token.SigningInput, token.Signature, hash, algorithm[0] == 'P' ? RSASignaturePadding.Pss : RSASignaturePadding.Pkcs1);
				break;
			case ('E', ECDsa ecdsa):
			{
				int expectedBits = algorithm switch
				{
					"ES256" => 256,
					"ES384" => 384,
					_ => 521,
				};

				if (ecdsa.KeySize != expectedBits)
				{
					return JwtVerification.NotChecked(string.Create(CultureInfo.InvariantCulture,
						$"{algorithm} needs a P-{expectedBits} key, and this {key.Source} is on a {ecdsa.KeySize}-bit curve."));
				}

				int expectedLength = 2 * ((expectedBits + 7) / 8);
				if (token.Signature.Length != expectedLength)
				{
					string shape = token.Signature.Length > 0 && token.Signature[0] == 0x30 ? " It looks DER encoded; JWS uses r and s side by side." : "";
					return new JwtVerification(JwtVerdict.Failed, string.Create(CultureInfo.InvariantCulture,
						$"The signature is {token.Signature.Length} bytes, and an {algorithm} signature is {expectedLength}.{shape}"), notes);
				}

				valid = ecdsa.VerifyData(token.SigningInput, token.Signature, hash, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
				break;
			}
			default:
				return JwtVerification.NotChecked($"{algorithm} needs {(algorithm[0] == 'E' ? "an EC" : "an RSA")} key, and this {key.Source} holds {Describe(key.Key)}.");
		}

		return valid
			? new JwtVerification(JwtVerdict.Verified, $"The signature matches: this token was signed with this key's private half, using {algorithm}.", notes)
			: new JwtVerification(JwtVerdict.Failed, "The signature does not match this key. The token was changed, or signed with another key.", notes);
	}

	private static JwtVerification? Unchecked(JwtToken token, JwtFamily family)
	{
		JwtFamily actual = FamilyOf(token.Algorithm);
		if (actual == JwtFamily.None)
		{
			return JwtVerification.NotChecked("alg is none: the token is not signed, so there is nothing to check. Never accept one.");
		}

		if (actual == JwtFamily.Unsupported)
		{
			return JwtVerification.NotChecked(token.Algorithm is null ? "The header names no algorithm." : $"{token.Algorithm} signatures are not checked here.");
		}

		return actual != family
			? JwtVerification.NotChecked(family == JwtFamily.Hmac
				? $"{token.Algorithm} is checked with a public key, not a shared secret."
				: $"{token.Algorithm} is checked with the shared secret, not a public key.")
			: null;
	}

	private static HashAlgorithmName HashOf(string algorithm) => algorithm[^3..] switch
	{
		"256" => HashAlgorithmName.SHA256,
		"384" => HashAlgorithmName.SHA384,
		_ => HashAlgorithmName.SHA512,
	};

	private static string Describe(AsymmetricAlgorithm key) => key switch
	{
		RSA rsa => string.Create(CultureInfo.InvariantCulture, $"an RSA {rsa.KeySize} key"),
		ECDsa ecdsa => string.Create(CultureInfo.InvariantCulture, $"an EC {ecdsa.KeySize} key"),
		_ => "another kind of key",
	};
}
