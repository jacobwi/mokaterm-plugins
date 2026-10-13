using System.Text;

namespace Moka.Certificates.Crypto;

/// <summary>
/// One certificate described for the user, the same way everywhere it shows: the Inspect view, a server's chain and the
/// System page. Built only by <see cref="CertificateReader.Describe(System.Security.Cryptography.X509Certificates.X509Certificate2, DateTimeOffset, int)"/>.
/// </summary>
public sealed record CertificateInfo
{
	/// <summary>The subject as one line, most specific part first: "CN=example.com, O=Example, C=US".</summary>
	public required string Subject { get; init; }

	public required IReadOnlyList<NamePart> SubjectParts { get; init; }

	public required string Issuer { get; init; }

	public required IReadOnlyList<NamePart> IssuerParts { get; init; }

	/// <summary>A short name for lists: the common name, else the first DNS name, else the whole subject.</summary>
	public required string Title { get; init; }

	/// <summary>The issuer's common name, else its organization, else the whole issuer.</summary>
	public required string IssuerTitle { get; init; }

	/// <summary>Hex, as the certificate carries it.</summary>
	public required string SerialNumber { get; init; }

	public required int Version { get; init; }

	public required DateTimeOffset NotBefore { get; init; }

	public required DateTimeOffset NotAfter { get; init; }

	public required ExpiryState Expiry { get; init; }

	/// <summary>Whole days until <see cref="NotAfter"/>; negative once it passed.</summary>
	public required int DaysLeft { get; init; }

	public required IReadOnlyList<GeneralNameInfo> AlternativeNames { get; init; }

	public required KeyInfo Key { get; init; }

	public required string SignatureAlgorithm { get; init; }

	public required string SignatureAlgorithmOid { get; init; }

	public required IReadOnlyList<ExtensionInfo> Extensions { get; init; }

	/// <summary>Basic constraints say it is a CA.</summary>
	public required bool IsCertificateAuthority { get; init; }

	/// <summary>Its subject and issuer are the same name, as a root's are.</summary>
	public required bool IsSelfIssued { get; init; }

	/// <summary>A self-issued certificate whose signature verifies with its own key. Null when the algorithm cannot be checked here.</summary>
	public required bool? IsSelfSigned { get; init; }

	public required string Sha256 { get; init; }

	public required string Sha1 { get; init; }

	/// <summary>The certificate's bytes, for PEM and DER output.</summary>
	public required byte[] Der { get; init; }

	// Certificate contents never go to a log through a record's generated ToString.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Sha256 = ").Append(Sha256);
		return true;
	}
}
