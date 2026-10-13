using Moka.Certificates.Crypto;

namespace Moka.Certificates.Tls;

/// <summary>A server check judged: what the view shows, built from a <see cref="TlsObservation"/> by <see cref="TlsReportBuilder"/>.</summary>
public sealed record TlsReport
{
	/// <summary>"example.com:443 (93.184.216.34)".</summary>
	public required string Endpoint { get; init; }

	/// <summary>The SNI name sent, or null when none was.</summary>
	public string? ServerName { get; init; }

	/// <summary>"TLS 1.3".</summary>
	public required string Protocol { get; init; }

	/// <summary>The cipher suite's IANA name, such as TLS_AES_256_GCM_SHA384.</summary>
	public required string Cipher { get; init; }

	public required CertificateInfo Leaf { get; init; }

	/// <summary>What the server sent: its certificate first.</summary>
	public required IReadOnlyList<CertificateInfo> Sent { get; init; }

	/// <summary>The chain this machine built, from the leaf to a root.</summary>
	public required IReadOnlyList<CertificateInfo> Chain { get; init; }

	public required string NameChecked { get; init; }

	public required HostNameResult NameMatch { get; init; }

	/// <summary>This machine trusts the chain, the name matches and the certificate is in date.</summary>
	public required bool Trusted { get; init; }

	/// <summary>What would make a client refuse the connection.</summary>
	public required IReadOnlyList<string> Problems { get; init; }

	/// <summary>What works but should be fixed, or is worth knowing.</summary>
	public required IReadOnlyList<string> Warnings { get; init; }

	/// <summary>How the sent certificates link up, when the server sent more than one.</summary>
	public ChainCheck? SentOrder { get; init; }

	/// <summary>The sent certificates as PEM, in the order sent: what to compare with the server's configured chain.</summary>
	public required string SentPem { get; init; }

	/// <summary>The built chain as PEM, root included.</summary>
	public required string ChainPem { get; init; }

	public required TimeSpan Elapsed { get; init; }
}
