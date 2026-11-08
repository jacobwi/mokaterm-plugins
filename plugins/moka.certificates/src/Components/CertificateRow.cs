using Moka.Certificates.Crypto;

namespace Moka.Certificates.Components;

/// <summary>One certificate of a chain as a <see cref="CertificateTable"/> lists it: where it sits and what it is there.</summary>
/// <param name="Position">From 0, the server's or the file's own certificate first.</param>
/// <param name="Role">"Server", "Intermediate", "Root" or "Chain".</param>
public sealed record CertificateRow(int Position, string Role, CertificateInfo Certificate)
{
	/// <summary>The rows of a chain read in order: the first is the server's, a self-issued last one is the root.</summary>
	public static IReadOnlyList<CertificateRow> Of(IReadOnlyList<CertificateInfo> certificates) =>
	[
		.. certificates.Select((certificate, position) => new CertificateRow(
			position,
			position == 0 ? "Server" : position == certificates.Count - 1 && certificate.IsSelfIssued ? "Root" : "Intermediate",
			certificate)),
	];
}
