using System.Text;

namespace Moka.Certificates.Crypto;

/// <summary>
/// What checking a CMS signature found. It checks the signatures against the signers' keys, not whether anyone trusts
/// the signers.
/// </summary>
public sealed record CmsVerification
{
	/// <summary>Every signature holds, and the expected signer (if one was given) is among them.</summary>
	public required bool Valid { get; init; }

	public required bool Detached { get; init; }

	public required IReadOnlyList<CmsSignerResult> Signers { get; init; }

	/// <summary>What an attached signature carries; null for a detached one.</summary>
	public byte[]? Content { get; init; }

	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Valid = ").Append(Valid).Append(", Signers = ").Append(Signers.Count);
		return true;
	}
}
