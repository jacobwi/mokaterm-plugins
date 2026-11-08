using System.Diagnostics.CodeAnalysis;

namespace Moka.DevTools.Hashing;

[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "The SHA-3 names follow .NET's own HashAlgorithmName.SHA3_256, where 3 and 256 would otherwise run together.")]
public enum HashKind
{
	Md5,
	Sha1,
	Sha256,
	Sha384,
	Sha512,
	Sha3_256,
	Sha3_384,
	Sha3_512,
	Crc32,
}
