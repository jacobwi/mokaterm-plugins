using System.Security.Authentication;

namespace Moka.Certificates.Tls;

/// <summary>The TLS versions the check offers one at a time, oldest first, to learn which the server takes.</summary>
public static class TlsVersions
{
	// TLS 1.0 and 1.1 are written by number: their enum members are marked obsolete, and the check still has to offer them.
	public static readonly IReadOnlyList<SslProtocols> Probed = [(SslProtocols)192, (SslProtocols)768, SslProtocols.Tls12, SslProtocols.Tls13];
}

/// <summary>What one handshake that offered only a single TLS version showed.</summary>
public enum TlsVersionOutcome
{
	/// <summary>The handshake completed, so the server takes this version.</summary>
	Accepted,

	/// <summary>The server would not complete it: it refused, closed the connection or never answered.</summary>
	Refused,

	/// <summary>This machine refused to offer the version at all, so the check cannot say whether the server would take it.</summary>
	NotOfferedHere,
}

/// <summary>One TLS version and what a handshake pinned to it showed, judged by <see cref="TlsReportBuilder"/>.</summary>
public sealed record TlsVersionResult(SslProtocols Protocol, TlsVersionOutcome Outcome);
