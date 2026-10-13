using System.Text;
using Moka.Certificates.Crypto;

namespace Moka.Certificates.Tool;

/// <summary>What one Generate run made, as the text and bytes the outputs show and save.</summary>
public sealed record GeneratedOutput
{
	public string Title { get; init; } = "certificate";

	public CertificateInfo? Certificate { get; init; }

	public string? CertificatePem { get; init; }

	/// <summary>A secret.</summary>
	public string? PrivateKeyPem { get; init; }

	public bool PrivateKeyEncrypted { get; init; }

	public string? KeyDescription { get; init; }

	public string? PublicKeyPem { get; init; }

	public string? RequestPem { get; init; }

	public byte[]? RequestDer { get; init; }

	/// <summary>The CA that signed it, as PEM, for a certificate a loaded CA signed.</summary>
	public string? ChainPem { get; init; }

	public IReadOnlyList<string> Notes { get; init; } = [];

	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Title = ").Append(Title).Append(", PrivateKey = ").Append(PrivateKeyPem is null ? "none" : "(set)");
		return true;
	}
}
