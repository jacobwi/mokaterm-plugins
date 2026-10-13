using System.Text;

namespace Moka.DevTools.Jwt;

/// <summary>
/// A JWS in compact form, read: the header and payload as formatted JSON, the claims, the times, the signature and the
/// bytes it signs. It holds the token's contents, so its ToString prints only the algorithm and counts.
/// </summary>
public sealed record JwtToken
{
	public required string HeaderJson { get; init; }

	public string? Algorithm { get; init; }

	public string? Type { get; init; }

	public string? KeyId { get; init; }

	/// <summary>The payload as formatted JSON; null when it is not JSON.</summary>
	public string? PayloadJson { get; init; }

	/// <summary>A payload that is not JSON: its text when it is UTF-8, otherwise hex.</summary>
	public string? PayloadText { get; init; }

	/// <summary>The payload's top-level members, when it is a JSON object.</summary>
	public IReadOnlyList<JwtClaim> Claims { get; init; } = [];

	public IReadOnlyList<JwtTime> Times { get; init; } = [];

	public required byte[] Signature { get; init; }

	/// <summary>The signature as the token carries it, Base64url.</summary>
	public required string SignatureText { get; init; }

	/// <summary>The ASCII of header.payload, which is what the signature covers.</summary>
	public required byte[] SigningInput { get; init; }

	public IReadOnlyList<string> Notes { get; init; } = [];

	public bool IsUnsigned => string.Equals(Algorithm, "none", StringComparison.OrdinalIgnoreCase);

	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Algorithm = ").Append(Algorithm).Append(", Claims = ").Append(Claims.Count)
			.Append(", Signature = ").Append(Signature.Length).Append(" bytes");
		return true;
	}
}
