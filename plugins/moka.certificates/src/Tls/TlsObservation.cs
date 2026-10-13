using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Tls;

/// <summary>
/// What one TLS handshake showed, recorded as it was and judged later by <see cref="TlsReportBuilder"/>: the
/// certificates the server sent, the chain this machine built from them, what the platform's validation said, and the
/// protocol and cipher agreed. Owns copies of every certificate.
/// </summary>
public sealed class TlsObservation : IDisposable
{
	public required TlsTarget Target { get; init; }

	public IPAddress? RemoteAddress { get; init; }

	public required SslProtocols Protocol { get; init; }

	public TlsCipherSuite? CipherSuite { get; init; }

	/// <summary>The server's certificate, then the others it sent in the order they came.</summary>
	public required IReadOnlyList<X509Certificate2> SentCertificates { get; init; }

	/// <summary>The chain this machine built from the leaf up, missing intermediates fetched and its trust store asked.</summary>
	public required IReadOnlyList<X509Certificate2> BuiltChain { get; init; }

	/// <summary>Everything the chain build complained about, all elements together.</summary>
	public required X509ChainStatusFlags ChainStatus { get; init; }

	public required SslPolicyErrors PolicyErrors { get; init; }

	public required TimeSpan Elapsed { get; init; }

	public void Dispose()
	{
		foreach (X509Certificate2 certificate in SentCertificates.Concat(BuiltChain))
		{
			certificate.Dispose();
		}
	}
}
