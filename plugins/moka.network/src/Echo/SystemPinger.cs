using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;

namespace Moka.Network.Echo;

/// <summary>
/// Echo requests through <see cref="Ping"/>. On Windows that is the ICMP API; on Linux .NET uses a raw or ICMP datagram
/// socket when it may, and otherwise runs the system's ping program and reads what it prints.
/// </summary>
public sealed class SystemPinger : IPinger
{
	private static readonly byte[] Pattern = "abcdefghijklmnopqrstuvwabcdefghi"u8.ToArray();

	public static SystemPinger Instance { get; } = new();

	public async Task<EchoReply> SendAsync(IPAddress address, EchoRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(address);
		ArgumentNullException.ThrowIfNull(request);
		byte[] payload = Payload(Math.Clamp(request.PayloadSize, EchoRequest.MinPayload, EchoRequest.MaxPayload));
		PingOptions options = new(Math.Clamp(request.Ttl, EchoRequest.MinTtl, EchoRequest.MaxTtl), dontFragment: false);
		using Ping ping = new();
		long started = Stopwatch.GetTimestamp();
		PingReply reply;
		try
		{
			reply = await ping.SendPingAsync(address, request.Timeout, payload, options, cancellationToken);
		}
		catch (PingException) when (cancellationToken.IsCancellationRequested)
		{
			throw new OperationCanceledException(cancellationToken);
		}
		catch (PingException ex)
		{
			return new EchoReply(EchoStatus.Failed, null, null, Describe(ex));
		}

		cancellationToken.ThrowIfCancellationRequested();
		double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

		// The OS figure is whole milliseconds and reads 0 below one, and for an expired TTL it may be missing; the
		// stopwatch around the call fills those in. It is not used for a normal reply, because behind the Linux ping
		// program fallback it would include starting that program.
		double milliseconds = reply.RoundtripTime > 0 ? reply.RoundtripTime : elapsed;
		return reply.Status switch
		{
			IPStatus.Success => new EchoReply(EchoStatus.Reply, Named(reply.Address), milliseconds),
			IPStatus.TtlExpired or IPStatus.TimeExceeded => new EchoReply(EchoStatus.TtlExpired, HopAddress(reply.Address, address), milliseconds),
			IPStatus.TimedOut => EchoReply.TimedOut,
			IPStatus.DestinationHostUnreachable => Unreachable(reply, "host unreachable"),
			IPStatus.DestinationNetworkUnreachable => Unreachable(reply, "network unreachable"),
			IPStatus.DestinationPortUnreachable => Unreachable(reply, "port unreachable"),
			IPStatus.DestinationProtocolUnreachable => Unreachable(reply, "prohibited or protocol unreachable"),
			IPStatus.DestinationUnreachable => Unreachable(reply, "unreachable"),
			IPStatus.DestinationScopeMismatch => Unreachable(reply, "outside this link's scope"),
			IPStatus.PacketTooBig => new EchoReply(EchoStatus.Failed, Named(reply.Address), null, "too big for a link on the way; try a smaller payload"),
			IPStatus.TtlReassemblyTimeExceeded => new EchoReply(EchoStatus.Failed, Named(reply.Address), null, "fragments were lost on the way"),
			IPStatus.BadDestination => new EchoReply(EchoStatus.Failed, null, null, "this address cannot be pinged"),
			_ => new EchoReply(EchoStatus.Failed, Named(reply.Address), null, reply.Status.ToString()),
		};
	}

	/// <summary>The request's data: the alphabet Windows ping sends, repeated to the size asked for.</summary>
	internal static byte[] Payload(int size)
	{
		byte[] payload = new byte[size];
		for (int index = 0; index < size; index++)
		{
			payload[index] = Pattern[index % Pattern.Length];
		}

		return payload;
	}

	/// <summary>
	/// The router behind an expired TTL. Only a router sends that, never the destination, so a reply that names the
	/// destination there is a fallback filling in the address it was given rather than the one that answered.
	/// </summary>
	internal static IPAddress? HopAddress(IPAddress? reported, IPAddress destination) =>
		Named(reported) is { } named && !named.Equals(destination) ? named : null;

	private static IPAddress? Named(IPAddress? address) =>
		address is null || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None) ? null : address;

	private static EchoReply Unreachable(PingReply reply, string what) => new(EchoStatus.Unreachable, Named(reply.Address), null, what);

	private static string Describe(PingException ex)
	{
		Exception inner = ex.InnerException ?? ex;
		return inner switch
		{
			UnauthorizedAccessException => "this account may not send ICMP, and the ping program fallback was refused",
			System.Net.Sockets.SocketException socket => $"the network refused the request ({socket.SocketErrorCode})",
			System.ComponentModel.Win32Exception => "the ping program could not be started",
			PlatformNotSupportedException => "this platform cannot send ICMP from here",
			_ => "the request could not be sent",
		};
	}
}
