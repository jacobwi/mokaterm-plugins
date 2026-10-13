using System.Security.Cryptography;
using System.Text;
using Moka.DevTools.Codecs;

namespace Moka.DevTools.Tests.Jwt;

/// <summary>Tokens signed here with keys the tests make, the way a JWS library would sign them.</summary>
internal static class TestTokens
{
	/// <summary>The token jwt.io shows by default, signed with HS256 and the secret "your-256-bit-secret".</summary>
	public const string JwtIo =
		"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";

	public const string Payload = """{"sub":"alice","role":"admin","exp":4102444800,"iat":1700000000}""";

	public static string Part(string json) => B64(Encoding.UTF8.GetBytes(json));

	public static string B64(byte[] bytes) => Base64Codec.Encode(bytes, urlSafe: true, padding: false);

	public static string Header(string algorithm, string? keyId = null) => keyId is null
		? $$"""{"alg":"{{algorithm}}","typ":"JWT"}"""
		: $$"""{"alg":"{{algorithm}}","typ":"JWT","kid":"{{keyId}}"}""";

	public static string Hmac(string algorithm, byte[] key, string payload = Payload)
	{
		string input = $"{Part(Header(algorithm))}.{Part(payload)}";
		byte[] data = Encoding.ASCII.GetBytes(input);
		byte[] mac = algorithm switch
		{
			"HS256" => HMACSHA256.HashData(key, data),
			"HS384" => HMACSHA384.HashData(key, data),
			_ => HMACSHA512.HashData(key, data),
		};

		return $"{input}.{B64(mac)}";
	}

	public static string Rsa(string algorithm, RSA rsa, string payload = Payload, string? keyId = null)
	{
		string input = $"{Part(Header(algorithm, keyId))}.{Part(payload)}";
		byte[] signature = rsa.SignData(Encoding.ASCII.GetBytes(input), HashOf(algorithm), algorithm[0] == 'P' ? RSASignaturePadding.Pss : RSASignaturePadding.Pkcs1);
		return $"{input}.{B64(signature)}";
	}

	public static string Ecdsa(string algorithm, ECDsa ecdsa, string payload = Payload, DSASignatureFormat format = DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
	{
		string input = $"{Part(Header(algorithm))}.{Part(payload)}";
		byte[] signature = ecdsa.SignData(Encoding.ASCII.GetBytes(input), HashOf(algorithm), format);
		return $"{input}.{B64(signature)}";
	}

	/// <summary>The same token with one character of its payload changed, so its signature no longer covers it.</summary>
	public static string Tampered(string token)
	{
		string[] parts = token.Split('.');
		string payload = Encoding.UTF8.GetString(Base64Codec.Decode(parts[1], urlSafe: true).Bytes).Replace("admin", "owner", StringComparison.Ordinal);
		return $"{parts[0]}.{Part(payload)}.{parts[2]}";
	}

	public static string RsaJwk(RSA rsa, string? keyId = null)
	{
		RSAParameters parameters = rsa.ExportParameters(false);
		string kid = keyId is null ? "" : ",\"kid\":\"" + keyId + "\"";
		return $$"""{"kty":"RSA","n":"{{B64(parameters.Modulus!)}}","e":"{{B64(parameters.Exponent!)}}"{{kid}}}""";
	}

	public static string EcJwk(ECDsa ecdsa, string curve)
	{
		ECParameters parameters = ecdsa.ExportParameters(false);
		return $$"""{"kty":"EC","crv":"{{curve}}","x":"{{B64(parameters.Q.X!)}}","y":"{{B64(parameters.Q.Y!)}}"}""";
	}

	private static HashAlgorithmName HashOf(string algorithm) => algorithm[^3..] switch
	{
		"256" => HashAlgorithmName.SHA256,
		"384" => HashAlgorithmName.SHA384,
		_ => HashAlgorithmName.SHA512,
	};
}
