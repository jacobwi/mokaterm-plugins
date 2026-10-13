using System.Text;

namespace Moka.Certificates.Crypto;

/// <summary>A certificate signing request described for the user.</summary>
public sealed record RequestInfo
{
	public required string Subject { get; init; }

	public required IReadOnlyList<NamePart> SubjectParts { get; init; }

	/// <summary>The common name, else the first DNS name, else the subject.</summary>
	public required string Title { get; init; }

	/// <summary>The names asked for in the request's extensions.</summary>
	public required IReadOnlyList<GeneralNameInfo> AlternativeNames { get; init; }

	public required KeyInfo Key { get; init; }

	public required string SignatureAlgorithm { get; init; }

	/// <summary>True when the request's signature verifies with its own key; null when the algorithm cannot be checked here.</summary>
	public required bool? SignatureValid { get; init; }

	/// <summary>The extensions the request asks for, described like a certificate's.</summary>
	public required IReadOnlyList<ExtensionInfo> Extensions { get; init; }

	public required string Sha256 { get; init; }

	public required byte[] Der { get; init; }

	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Sha256 = ").Append(Sha256);
		return true;
	}
}
