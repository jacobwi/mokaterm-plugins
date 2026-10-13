namespace Moka.Certificates.Crypto;

/// <summary>
/// Whether the certificate at <see cref="Parent"/> issued the one at <see cref="Child"/> (both 0-based positions in the
/// bundle): the child's issuer is the parent's subject, and the parent's key verifies the child's signature.
/// </summary>
public sealed record ChainLink(int Child, int Parent, bool NamesMatch, bool? SignatureValid)
{
	/// <summary>The link holds, or at least nothing says it does not (a signature that cannot be checked here).</summary>
	public bool Holds => NamesMatch && SignatureValid != false;
}
