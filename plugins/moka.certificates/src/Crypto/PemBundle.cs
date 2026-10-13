using System.Text;

namespace Moka.Certificates.Crypto;

/// <summary>A PFX taken apart: the certificate its key belongs to, the key, and the rest of the chain, as PEM.</summary>
public sealed record PemBundle
{
	public string? Certificate { get; init; }

	/// <summary>A secret: copied as one and never logged.</summary>
	public string? PrivateKey { get; init; }

	public string? Chain { get; init; }

	public IReadOnlyList<string> Notes { get; init; } = [];

	// The key is not something a record's generated ToString may print.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("PrivateKey = ").Append(PrivateKey is null ? "none" : "(set)");
		return true;
	}
}
