using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Moka.Network.Targets;

namespace Moka.Network.Echo;

/// <summary>
/// Echo requests as TCP connection attempts, for a host that silently drops ICMP: the time from starting the connection
/// to its outcome is the round trip, and nothing is sent once connected. A refusal counts as a reply, since only the
/// host itself can send it, and it carries the refusal's round trip. Of <see cref="EchoRequest"/> only the wait is
/// used: a payload and a time to live are ICMP ideas with no part in one connection attempt.
/// </summary>
public sealed class TcpPinger : IPinger
{
	public TcpPinger(int port) => Port = Math.Clamp(port, 1, 65535);

	/// <summary>The port every attempt dials.</summary>
	public int Port { get; }

	public async Task<EchoReply> SendAsync(IPAddress address, EchoRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(address);
		ArgumentNullException.ThrowIfNull(request);
		IPEndPoint endpoint = new(address, Port);
		long started = Stopwatch.GetTimestamp();
		using Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
		using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		limit.CancelAfter(request.Timeout);
		try
		{
			await socket.ConnectAsync(endpoint, limit.Token);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return EchoReply.TimedOut;
		}
		catch (SocketException ex)
		{
			return ex.SocketErrorCode switch
			{
				SocketError.ConnectionRefused => new EchoReply(EchoStatus.Reply, address, Elapsed(), "the port is closed, but the host answered"),
				SocketError.TimedOut => EchoReply.TimedOut,
				SocketError.NetworkUnreachable or SocketError.HostUnreachable or SocketError.HostDown => new EchoReply(EchoStatus.Unreachable, null, null, "no route to the machine"),
				SocketError.AddressNotAvailable or SocketError.AddressFamilyNotSupported => new EchoReply(EchoStatus.Failed, null, null, $"This machine has no {TargetResolver.FamilyName(address.AddressFamily)} route to use."),
				SocketError.AccessDenied => new EchoReply(EchoStatus.Failed, null, null, "This machine's firewall refused the connection."),
				_ => new EchoReply(EchoStatus.Failed, null, null, $"the connection failed ({ex.SocketErrorCode})"),
			};
		}

		return new EchoReply(EchoStatus.Reply, address, Elapsed());

		double Elapsed() => Stopwatch.GetElapsedTime(started).TotalMilliseconds;
	}
}
