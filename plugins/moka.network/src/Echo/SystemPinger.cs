using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;

namespace Moka.Network.Echo;

/// <summary>
/// Echo requests through <see cref="Ping"/>. On Windows that is the ICMP API. On Linux .NET opens a raw ICMP socket when
/// the process may, and otherwise runs the system's ping program and reads what it prints, which carries only .NET's own
/// payload (<see cref="BufferFor"/>).
/// </summary>
public sealed class SystemPinger : IPinger
{
	private static readonly byte[] Pattern = "abcdefghijklmnopqrstuvwabcdefghi"u8.ToArray();

	public static SystemPinger Instance { get; } = new();

	public async Task<EchoReply> SendAsync(IPAddress address, EchoRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(address);
		ArgumentNullException.ThrowIfNull(request);
		int size = Math.Clamp(request.PayloadSize, EchoRequest.MinPayload, EchoRequest.MaxPayload);
		byte[]? buffer = BufferFor(size);
		PingOptions options = new(Math.Clamp(request.Ttl, EchoRequest.MinTtl, EchoRequest.MaxTtl), request.DontFragment);
		using Ping ping = new();
		long started = Stopwatch.GetTimestamp();
		PingReply reply;
		try
		{
			reply = await ping.SendPingAsync(address, request.Timeout, buffer, options, cancellationToken);
		}
		catch (PingException) when (cancellationToken.IsCancellationRequested)
		{
			throw new OperationCanceledException(cancellationToken);
		}
		catch (PingException ex)
		{
			return new EchoReply(EchoStatus.Failed, null, null, Describe(ex));
		}
		catch (PlatformNotSupportedException) when (buffer is { Length: > 0 })
		{
			return new EchoReply(
				EchoStatus.NotSent,
				null,
				null,
				$"sending {size} bytes of data needs a raw ICMP socket, which this process cannot open; {EchoRequest.DefaultPayload} bytes or none go through the ping program");
		}
		catch (PlatformNotSupportedException)
		{
			return new EchoReply(EchoStatus.NotSent, null, null, "this platform cannot send ICMP from here");
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

	/// <summary>
	/// The buffer handed to <see cref="Ping"/>. .NET's Linux fallback to the ping program refuses every array but its own
	/// default payload and the shared empty one, even an array holding the same bytes (dotnet/runtime, Ping.PingUtility.cs).
	/// So the default size passes null, for which .NET sends its own 32 bytes of this same alphabet, and an empty payload
	/// passes <see cref="Array.Empty{T}"/> itself.
	/// </summary>
	internal static byte[]? BufferFor(int size) => size switch
	{
		EchoRequest.DefaultPayload => null,
		0 => Array.Empty<byte>(),
		_ => Payload(size),
	};

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
