using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Moka.DevTools.Codecs;

namespace Moka.DevTools.Jwt;

/// <summary>
/// Reads the public key a signature is checked with: a PEM public key (SPKI or PKCS#1), a PEM certificate, a JWK or a
/// JWK set. A private key is refused, since checking never needs one and pasting one here is a mistake worth saying.
/// </summary>
public static class JwtKeyReader
{
	private static readonly string[] PrivateLabels = ["PRIVATE KEY", "RSA PRIVATE KEY", "EC PRIVATE KEY", "ENCRYPTED PRIVATE KEY", "OPENSSH PRIVATE KEY"];

	public static JwtPublicKey Read(string text, string? keyId)
	{
		ArgumentNullException.ThrowIfNull(text);
		string value = text.Trim();
		if (value.Length == 0)
		{
			throw new JwtFormatException("Paste the public key, the certificate or the JWK to check with.");
		}

		return value[0] == '{' ? ReadJwk(value, keyId) : ReadPem(value);
	}

	private static JwtPublicKey ReadPem(string text)
	{
		ReadOnlySpan<char> rest = text;
		bool sawBlock = false;
		while (PemEncoding.TryFind(rest, out PemFields fields))
		{
			sawBlock = true;
			string label = rest[fields.Label].ToString();
			byte[] der = Convert.FromBase64String(rest[fields.Base64Data].ToString());
			rest = rest[fields.Location.End..];
			if (PrivateLabels.Contains(label, StringComparer.Ordinal))
			{
				throw new JwtFormatException("That is a private key. Checking a signature needs only the public key or the certificate.");
			}

			switch (label)
			{
				case "PUBLIC KEY":
					return new JwtPublicKey(FromSubjectPublicKeyInfo(der), "public key", []);
				case "RSA PUBLIC KEY":
				{
					RSA rsa = RSA.Create();
					try
					{
						rsa.ImportRSAPublicKey(der, out _);
					}
					catch (CryptographicException)
					{
						rsa.Dispose();
						throw new JwtFormatException("The RSA PUBLIC KEY block could not be read.");
					}

					return new JwtPublicKey(rsa, "RSA public key", []);
				}
				case "CERTIFICATE":
					return FromCertificate(der);
			}
		}

		throw new JwtFormatException(sawBlock
			? "No PUBLIC KEY, RSA PUBLIC KEY or CERTIFICATE block was found."
			: "That is not PEM (-----BEGIN PUBLIC KEY-----) or a JWK ({\"kty\": ...}).");
	}

	private static AsymmetricAlgorithm FromSubjectPublicKeyInfo(byte[] der)
	{
		RSA rsa = RSA.Create();
		try
		{
			rsa.ImportSubjectPublicKeyInfo(der, out _);
			return rsa;
		}
		catch (CryptographicException)
		{
			rsa.Dispose();
		}

		ECDsa ecdsa = ECDsa.Create();
		try
		{
			ecdsa.ImportSubjectPublicKeyInfo(der, out _);
			return ecdsa;
		}
		catch (CryptographicException)
		{
			ecdsa.Dispose();
			throw new JwtFormatException("The public key is neither RSA nor EC (an Ed25519 key, perhaps), so it cannot check RS, PS or ES signatures.");
		}
	}

	private static JwtPublicKey FromCertificate(byte[] der)
	{
		X509Certificate2 certificate;
		try
		{
			certificate = X509CertificateLoader.LoadCertificate(der);
		}
		catch (CryptographicException)
		{
			throw new JwtFormatException("The certificate could not be read.");
		}

		using (certificate)
		{
			AsymmetricAlgorithm key = (AsymmetricAlgorithm?)certificate.GetRSAPublicKey()
				?? certificate.GetECDsaPublicKey()
				?? throw new JwtFormatException("The certificate's key is neither RSA nor EC.");
			return new JwtPublicKey(key, "certificate", ["Only the certificate's key was used; its dates, names and issuer were not checked."]);
		}
	}

	private static JwtPublicKey ReadJwk(string text, string? keyId)
	{
		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(text);
		}
		catch (JsonException)
		{
			throw new JwtFormatException("That starts like a JWK but is not valid JSON.");
		}

		using (document)
		{
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				throw new JwtFormatException("A JWK is a JSON object.");
			}

			if (root.TryGetProperty("keys", out JsonElement keys) && keys.ValueKind == JsonValueKind.Array)
			{
				return FromJwk(Pick(keys, keyId), "JWK from the set");
			}

			return FromJwk(root, "JWK");
		}
	}

	private static JsonElement Pick(JsonElement keys, string? keyId)
	{
		List<JsonElement> all = [.. keys.EnumerateArray().Where(key => key.ValueKind == JsonValueKind.Object)];
		if (keyId is not null)
		{
			foreach (JsonElement key in all)
			{
				if (key.TryGetProperty("kid", out JsonElement kid) && kid.ValueKind == JsonValueKind.String && kid.GetString() == keyId)
				{
					return key;
				}
			}

			throw new JwtFormatException($"No key in the set has the kid the token names ({keyId}).");
		}

		return all.Count switch
		{
			0 => throw new JwtFormatException("The key set is empty."),
			1 => all[0],
			_ => throw new JwtFormatException($"The set holds {all.Count} keys and the token names no kid, so paste the one key to use."),
		};
	}

	private static JwtPublicKey FromJwk(JsonElement jwk, string source)
	{
		string kty = Member(jwk, "kty") ?? throw new JwtFormatException("The JWK has no kty.");
		if (jwk.TryGetProperty("d", out _))
		{
			throw new JwtFormatException("That JWK holds a private key (it has d). Checking a signature needs only the public part.");
		}

		switch (kty)
		{
			case "RSA":
			{
				RSAParameters parameters = new() { Modulus = Bytes(jwk, "n"), Exponent = Bytes(jwk, "e") };
				RSA rsa = RSA.Create();
				try
				{
					rsa.ImportParameters(parameters);
				}
				catch (CryptographicException)
				{
					rsa.Dispose();
					throw new JwtFormatException("The RSA JWK's n and e do not make a key.");
				}

				return new JwtPublicKey(rsa, source, []);
			}
			case "EC":
			{
				ECCurve curve = Member(jwk, "crv") switch
				{
					"P-256" => ECCurve.NamedCurves.nistP256,
					"P-384" => ECCurve.NamedCurves.nistP384,
					"P-521" => ECCurve.NamedCurves.nistP521,
					string other => throw new JwtFormatException($"The JWK's curve {other} is not one ES256, ES384 or ES512 uses."),
					null => throw new JwtFormatException("The EC JWK has no crv."),
				};

				ECParameters parameters = new() { Curve = curve, Q = new ECPoint { X = Bytes(jwk, "x"), Y = Bytes(jwk, "y") } };
				ECDsa ecdsa = ECDsa.Create();
				try
				{
					ecdsa.ImportParameters(parameters);
				}
				catch (CryptographicException)
				{
					ecdsa.Dispose();
					throw new JwtFormatException("The EC JWK's x and y are not a point on its curve.");
				}

				return new JwtPublicKey(ecdsa, source, []);
			}
			case "oct":
				throw new JwtFormatException("That is a shared secret (kty oct). HS tokens are checked with the secret itself: paste its k as a Base64 secret.");
			default:
				throw new JwtFormatException($"Keys of type {kty} are not checked here.");
		}
	}

	private static string? Member(JsonElement element, string name) =>
		element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

	private static byte[] Bytes(JsonElement element, string name)
	{
		string value = Member(element, name) ?? throw new JwtFormatException($"The JWK has no {name}.");
		try
		{
			return Base64Codec.Decode(value, urlSafe: true).Bytes;
		}
		catch (CodecException)
		{
			throw new JwtFormatException($"The JWK's {name} is not Base64url.");
		}
	}
}
