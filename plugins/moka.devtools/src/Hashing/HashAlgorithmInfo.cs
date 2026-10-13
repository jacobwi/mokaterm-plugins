using System.Security.Cryptography;

namespace Moka.DevTools.Hashing;

/// <summary>One algorithm the Hashes section offers, and whether this platform can run it, plain and as an HMAC.</summary>
internal sealed record HashAlgorithmInfo(HashKind Kind, string Name, HashAlgorithmName? Algorithm, bool Weak, bool Supported, bool HmacSupported)
{
	/// <summary>Every algorithm in the order the section lists them. SHA-3 depends on the OS's crypto library.</summary>
	public static IReadOnlyList<HashAlgorithmInfo> All { get; } =
	[
		new(HashKind.Md5, "MD5", HashAlgorithmName.MD5, Weak: true, Supported: true, HmacSupported: true),
		new(HashKind.Sha1, "SHA-1", HashAlgorithmName.SHA1, Weak: true, Supported: true, HmacSupported: true),
		new(HashKind.Sha256, "SHA-256", HashAlgorithmName.SHA256, Weak: false, Supported: true, HmacSupported: true),
		new(HashKind.Sha384, "SHA-384", HashAlgorithmName.SHA384, Weak: false, Supported: true, HmacSupported: true),
		new(HashKind.Sha512, "SHA-512", HashAlgorithmName.SHA512, Weak: false, Supported: true, HmacSupported: true),
		new(HashKind.Sha3_256, "SHA3-256", HashAlgorithmName.SHA3_256, Weak: false, SHA3_256.IsSupported, HMACSHA3_256.IsSupported),
		new(HashKind.Sha3_384, "SHA3-384", HashAlgorithmName.SHA3_384, Weak: false, SHA3_384.IsSupported, HMACSHA3_384.IsSupported),
		new(HashKind.Sha3_512, "SHA3-512", HashAlgorithmName.SHA3_512, Weak: false, SHA3_512.IsSupported, HMACSHA3_512.IsSupported),
		new(HashKind.Crc32, "CRC32", null, Weak: true, Supported: true, HmacSupported: false),
	];

	/// <summary>Whether this algorithm runs here in the mode asked for.</summary>
	public bool Runs(bool hmac) => hmac ? HmacSupported : Supported;
}
