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
/// In a STARTTLS mode the handshake is preceded by that mode's little line exchange. The validation callback records
/// what the platform found and accepts the certificate either way, because a refused handshake would end before the
/// protocol and cipher are known; what it found is judged afterwards by <see cref="TlsReportBuilder"/>. Revocation is
/// not checked, so the check makes no OCSP or CRL requests.
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
			using TcpClient client = await ConnectAsync(target, limit.Token);
			await StartTlsExchange.ExchangeAsync(target.StartTls, client.GetStream(), limit.Token);
			IPAddress? remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
			await using SslStream ssl = new(client.GetStream(), leaveInnerStreamOpen: false);
			SslClientAuthenticationOptions options = ClientOptions(target, recorder.Record);
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

	/// <summary>
	/// One short-lived handshake per TLS version in <see cref="TlsVersions.Probed"/>, each offering that version alone,
	/// to learn which versions the server takes. The certificates these see are thrown away: the chain report comes
	/// from the unpinned handshake alone.
	/// </summary>
	public static async Task<IReadOnlyList<TlsVersionResult>> ProbeVersionsAsync(TlsTarget target, TimeSpan timeout, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(target);
		List<TlsVersionResult> results = [];
		foreach (SslProtocols version in TlsVersions.Probed)
		{
			results.Add(new(version, await ProbeVersionAsync(target, version, timeout, cancellationToken)));
		}

		return results;
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5359:Do Not Disable Certificate Validation", Justification = "The check reports on the certificate rather than trusting it: these handshakes record nothing and only answer whether the version completed.")]
	private static async Task<TlsVersionOutcome> ProbeVersionAsync(TlsTarget target, SslProtocols version, TimeSpan timeout, CancellationToken cancellationToken)
	{
		using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		limit.CancelAfter(timeout);
		CountingStream? counted = null;
		try
		{
			using TcpClient client = await ConnectAsync(target, limit.Token);
			await StartTlsExchange.ExchangeAsync(target.StartTls, client.GetStream(), limit.Token);
			counted = new CountingStream(client.GetStream());
			await using SslStream ssl = new(counted, leaveInnerStreamOpen: true);
			SslClientAuthenticationOptions options = ClientOptions(target, AcceptAnything);
			// Offering one version alone: the server either takes it or ends the handshake.
			options.EnabledSslProtocols = version;
			await ssl.AuthenticateAsClientAsync(options, limit.Token);
			return TlsVersionOutcome.Accepted;
		}
		catch (Exception ex) when (ex is AuthenticationException or IOException or SocketException or OperationCanceledException)
		{
			return Classify(ex, counted?.WroteAnything == true);
		}
	}

	/// <summary>
	/// How a pinned handshake's failure reads. A failure before a single byte went out means this machine's own TLS
	/// policy refused to offer the version, so the server was never asked and the check cannot say; once the offer was
	/// sent, any failure is the server's answer (or its silence) to a version it was asked for.
	/// </summary>
	internal static TlsVersionOutcome Classify(Exception failure, bool offered) =>
		failure is AuthenticationException && !offered ? TlsVersionOutcome.NotOfferedHere : TlsVersionOutcome.Refused;

	private static bool AcceptAnything(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors) => true;

	private static async Task<TcpClient> ConnectAsync(TlsTarget target, CancellationToken cancellationToken)
	{
		TcpClient client = new();
		try
		{
			await client.ConnectAsync(target.Host, target.Port, cancellationToken);
			return client;
		}
		catch
		{
			client.Dispose();
			throw;
		}
	}

	private static SslClientAuthenticationOptions ClientOptions(TlsTarget target, RemoteCertificateValidationCallback validation) => new()
	{
		// An empty name sends no SNI, which is right for a bare address.
		TargetHost = target.ServerName ?? "",
		RemoteCertificateValidationCallback = validation,
		CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
		AllowRenegotiation = false,
	};

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

	/// <summary>Wraps the connection so the probe can tell a failure before anything was sent from one the server answered.</summary>
	private sealed class CountingStream(Stream inner) : Stream
	{
		public bool WroteAnything { get; private set; }

		public override bool CanRead => inner.CanRead;

		public override bool CanWrite => inner.CanWrite;

		public override bool CanSeek => inner.CanSeek;

		public override long Length => inner.Length;

		public override long Position { get => inner.Position; set => inner.Position = value; }

		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) => inner.ReadAsync(buffer, cancellationToken);

		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
		{
			WroteAnything = true;
			return inner.WriteAsync(buffer, cancellationToken);
		}

		public override void Flush() => inner.Flush();

		public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

		public override void Write(byte[] buffer, int offset, int count)
		{
			WroteAnything = true;
			inner.Write(buffer, offset, count);
		}

		public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

		public override void SetLength(long value) => inner.SetLength(value);
	}
}
