using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Moka.Certificates.Crypto;

namespace Moka.Certificates.Tls;

/// <summary>
/// Connects to a server, completes one TLS handshake and closes: no client certificate, no application data, no retry.
/// The validation callback records what the platform found and accepts the certificate either way, because a refused
/// handshake would end before the protocol and cipher are known; what it found is judged afterwards by
/// <see cref="TlsReportBuilder"/>. Revocation is not checked, so the check makes no OCSP or CRL requests.
/// </summary>
public static class TlsProbe
{
	[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5359:Do Not Disable Certificate Validation", Justification = "The check reports on the certificate rather than trusting it: the callback records the platform's verdict, nothing is sent over the connection, and TlsReportBuilder judges what was recorded.")]
	public static async Task<TlsObservation> ProbeAsync(TlsTarget target, TimeSpan timeout, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(target);
		using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		limit.CancelAfter(timeout);
		Stopwatch watch = Stopwatch.StartNew();
		TlsRecorder recorder = new();
		try
		{
			using TcpClient client = new();
			await client.ConnectAsync(target.Host, target.Port, limit.Token);
			IPAddress? remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
			await using SslStream ssl = new(client.GetStream(), leaveInnerStreamOpen: false);
			SslClientAuthenticationOptions options = new()
			{
				// An empty name sends no SNI, which is right for a bare address.
				TargetHost = target.ServerName ?? "",
				RemoteCertificateValidationCallback = recorder.Record,
				CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
				AllowRenegotiation = false,
			};

			await ssl.AuthenticateAsClientAsync(options, limit.Token);
			watch.Stop();
			return recorder.ToObservation(target, remote, ssl.SslProtocol, ssl.NegotiatedCipherSuite, watch.Elapsed);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new CertificateException($"{target.Endpoint} did not answer within {timeout.TotalSeconds:0} s.");
		}
		catch (SocketException ex)
		{
			throw new CertificateException(Describe(ex, target), ex);
		}
		catch (AuthenticationException ex)
		{
			throw new CertificateException($"{target.Endpoint} did not complete a TLS handshake: {FirstLine(ex.Message)}", ex);
		}
		catch (IOException ex)
		{
			throw new CertificateException($"{target.Endpoint} closed the connection during the handshake. It may not speak TLS on this port.", ex);
		}
		finally
		{
			// Does nothing once the certificates were handed to the observation.
			recorder.Dispose();
		}
	}

	private static string Describe(SocketException ex, TlsTarget target) => ex.SocketErrorCode switch
	{
		SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => $"{target.Host} could not be found in DNS.",
		SocketError.ConnectionRefused => $"{target.Endpoint} refused the connection: nothing listens on that port.",
		SocketError.TimedOut => $"{target.Endpoint} did not answer.",
		SocketError.NetworkUnreachable or SocketError.HostUnreachable => $"{target.Endpoint} cannot be reached from this machine.",
		SocketError.ConnectionReset => $"{target.Endpoint} reset the connection.",
		_ => $"{target.Endpoint} could not be reached ({ex.SocketErrorCode}).",
	};

	private static string FirstLine(string message)
	{
		int end = message.AsSpan().IndexOfAny('\r', '\n');
		return (end < 0 ? message : message[..end]).Trim();
	}
}
