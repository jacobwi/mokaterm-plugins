using System.Security.Cryptography;

namespace Moka.DevTools.Jwt;

/// <summary>A public key read for checking a signature, and where it came from ("certificate", "JWK").</summary>
public sealed class JwtPublicKey(AsymmetricAlgorithm key, string source, IReadOnlyList<string> notes) : IDisposable
{
	public AsymmetricAlgorithm Key { get; } = key;

	public string Source { get; } = source;

	public IReadOnlyList<string> Notes { get; } = notes;

	public void Dispose() => Key.Dispose();
}
