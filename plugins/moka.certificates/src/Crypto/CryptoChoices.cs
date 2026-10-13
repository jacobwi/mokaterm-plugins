using System.Security.Cryptography;

namespace Moka.Certificates.Crypto;

/// <summary>What each choice the views and settings offer means to .NET, and what it is called on screen.</summary>
public static class CryptoChoices
{
	public static int Bits(RsaKeySize size) => size switch
	{
		RsaKeySize.Bits3072 => 3072,
		RsaKeySize.Bits4096 => 4096,
		_ => 2048,
	};

	public static ECCurve Curve(EllipticCurve curve) => curve switch
	{
		EllipticCurve.P384 => ECCurve.NamedCurves.nistP384,
		EllipticCurve.P521 => ECCurve.NamedCurves.nistP521,
		_ => ECCurve.NamedCurves.nistP256,
	};

	public static string CurveName(EllipticCurve curve) => curve switch
	{
		EllipticCurve.P384 => "P-384",
		EllipticCurve.P521 => "P-521",
		_ => "P-256",
	};

	public static HashAlgorithmName Hash(SignatureHash hash) => hash switch
	{
		SignatureHash.Sha384 => HashAlgorithmName.SHA384,
		SignatureHash.Sha512 => HashAlgorithmName.SHA512,
		_ => HashAlgorithmName.SHA256,
	};

	public static string HashName(SignatureHash hash) => hash switch
	{
		SignatureHash.Sha384 => "SHA-384",
		SignatureHash.Sha512 => "SHA-512",
		_ => "SHA-256",
	};

	public static string AlgorithmName(KeyAlgorithm algorithm) => algorithm == KeyAlgorithm.Rsa ? "RSA" : "ECDSA";

	public static string SizeName(RsaKeySize size) => Bits(size).ToString(System.Globalization.CultureInfo.InvariantCulture);

	public static string PfxEncryptionName(PfxEncryption encryption) => encryption == PfxEncryption.Legacy3Des
		? "3DES and SHA-1 (older Windows)"
		: "AES-256 and SHA-256";

	public static string Describe(KeySpec spec)
	{
		ArgumentNullException.ThrowIfNull(spec);
		return spec.Algorithm == KeyAlgorithm.Rsa ? $"RSA {SizeName(spec.RsaSize)}" : $"ECDSA {CurveName(spec.Curve)}";
	}
}
