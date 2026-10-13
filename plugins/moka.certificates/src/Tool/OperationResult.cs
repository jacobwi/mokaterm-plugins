using System.Text;
using Moka.Certificates.Crypto;

namespace Moka.Certificates.Tool;

/// <summary>What a sign, verify, encrypt or decrypt run shows: a verdict, the signers of a CMS signature, and an output.</summary>
public sealed record OperationResult
{
	/// <summary>True or false for a verification; null for anything that makes an output.</summary>
	public bool? Verdict { get; init; }

	public string? Summary { get; init; }

	public IReadOnlyList<CmsSignerResult> Signers { get; init; } = [];

	public string TextTitle { get; init; } = "Result";

	/// <summary>Text to show and copy: a signature, a PEM structure, decrypted text.</summary>
	public string? Text { get; init; }

	public string? TextFileName { get; init; }

	/// <summary>The same result as bytes to save, such as a DER signature or decrypted binary content.</summary>
	public byte[]? Bytes { get; init; }

	public string? BytesFileName { get; init; }

	public string BytesLabel { get; init; } = "Save DER";

	// Decrypted content can be anything the user encrypted; nothing prints it.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Verdict = ").Append(Verdict).Append(", Text = ").Append(Text?.Length ?? 0).Append(" chars");
		return true;
	}
}
