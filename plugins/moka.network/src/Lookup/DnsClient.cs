using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Mokaterm.Abstractions.Protocols;

namespace Moka.Network.Lookup;

/// <summary>
/// Asks one server one question: over UDP first, sent again halfway through the time limit since a datagram can be
/// lost, and over TCP when the UDP reply comes back cut (TC). A UDP reply is taken only from the server asked (the
/// socket is connected), with the query's id and its exact question; anything else is passed over, up to a limit.
/// </summary>
public static class DnsClient
{
	/// <summary>The largest UDP payload there is, so a datagram is never cut by the receive buffer.</summary>
	public const int MaxUdpReply = 65535;

	/// <summary>Stray datagrams passed over before giving up: someone is answering, but not this question.</summary>
	public const int MaxIgnoredReplies = 32;

	public static async Task<DnsExchange> ExchangeAsync(IPEndPoint server, string name, int type, TimeSpan timeout, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(server);
		ArgumentNullException.ThrowIfNull(name);
		string endpoint = Describe(server);
		long started = Stopwatch.GetTimestamp();
		try
		{
			(DnsMessage reply, int ignored) = await OverUdpAsync(server, name, type, timeout, cancellationToken);
			if (!reply.Header.Truncated)
			{
				return new DnsExchange { Message = reply, Server = server, Elapsed = Stopwatch.GetElapsedTime(started), IgnoredReplies = ignored };
			}

			DnsMessage full = await OverTcpAsync(server, name, type, timeout, cancellationToken);
			return new DnsExchange { Message = full, Server = server, Elapsed = Stopwatch.GetElapsedTime(started), OverTcp = true, IgnoredReplies = ignored };
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new DnsLookupException($"{endpoint} did not answer within {timeout.TotalSeconds:0.#} s.");
		}
		catch (SocketException ex)
		{
			throw new DnsLookupException(DescribeFailure(ex, endpoint), ex);
		}
		catch (IOException ex)
		{
			throw new DnsLookupException($"{endpoint} closed the connection before it answered.", ex);
		}
		catch (DnsFormatException ex)
		{
			throw new DnsLookupException($"The answer from {endpoint} could not be read: {ex.Message}", ex);
		}
	}

	/// <summary>A server as the view names it: <c>1.1.1.1:53</c>, <c>[2606:4700:4700::1111]:53</c>.</summary>
	public static string Describe(IPEndPoint server)
	{
		ArgumentNullException.ThrowIfNull(server);
		return HostEndpoint.Format(server.Address.ToString(), server.Port);
	}

	private static async Task<(DnsMessage Reply, int Ignored)> OverUdpAsync(IPEndPoint server, string name, int type, TimeSpan timeout, CancellationToken cancellationToken)
	{
		ushort id = DnsQueryBuilder.NewId();
		byte[] query = DnsQueryBuilder.Build(id, name, type);
		using Socket socket = new(server.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
		using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		limit.CancelAfter(timeout);
		await socket.ConnectAsync(server, limit.Token);
		await socket.SendAsync(query, SocketFlags.None, limit.Token);
		Task resend = ResendAsync(socket, query, timeout / 2, limit.Token);

		byte[] buffer = new byte[MaxUdpReply];
		int ignored = 0;
		try
		{
			while (true)
			{
				int length = await socket.ReceiveAsync(buffer, SocketFlags.None, limit.Token);
				if (length < DnsMessageParser.HeaderSize || BinaryPrimitives.ReadUInt16BigEndian(buffer) != id)
				{
					CountIgnored(ref ignored, server);
					continue;
				}

				DnsMessage reply = DnsMessageParser.Parse(buffer, length);
				if (DnsMessageParser.Answers(reply, id, name, type))
				{
					return (reply, ignored);
				}

				CountIgnored(ref ignored, server);
			}
		}
		finally
		{
			await limit.CancelAsync();
			await resend;
		}
	}

	private static async Task<DnsMessage> OverTcpAsync(IPEndPoint server, string name, int type, TimeSpan timeout, CancellationToken cancellationToken)
	{
		ushort id = DnsQueryBuilder.NewId();
		byte[] query = DnsQueryBuilder.Build(id, name, type);
		using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		limit.CancelAfter(timeout);
		using Socket socket = new(server.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
		await socket.ConnectAsync(server, limit.Token);
		await using NetworkStream stream = new(socket, ownsSocket: false);

		byte[] framed = new byte[query.Length + 2];
		BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)query.Length);
		query.CopyTo(framed, 2);
		await stream.WriteAsync(framed, limit.Token);

		// The length prefix is two bytes, so a reply can never be bigger than the buffer it asks for.
		byte[] prefix = new byte[2];
		await stream.ReadExactlyAsync(prefix, limit.Token);
		int length = BinaryPrimitives.ReadUInt16BigEndian(prefix);
		if (length < DnsMessageParser.HeaderSize)
		{
			throw new DnsFormatException($"The TCP reply is {length} bytes, shorter than a DNS header.");
		}

		byte[] buffer = new byte[length];
		await stream.ReadExactlyAsync(buffer, limit.Token);
		DnsMessage reply = DnsMessageParser.Parse(buffer, length);
		if (!DnsMessageParser.Answers(reply, id, name, type))
		{
			throw new DnsLookupException($"{Describe(server)} answered over TCP with another id or another question.");
		}

		return reply;
	}

	private static async Task ResendAsync(Socket socket, byte[] query, TimeSpan delay, CancellationToken cancellationToken)
	{
		try
		{
			await Task.Delay(delay, cancellationToken);
			await socket.SendAsync(query, SocketFlags.None, cancellationToken);
		}
		catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
		{
			// The answer came first, the time ran out, or the network refused the second copy; the first one stands.
		}
	}

	private static void CountIgnored(ref int ignored, IPEndPoint server)
	{
		if (++ignored > MaxIgnoredReplies)
		{
			throw new DnsLookupException($"{Describe(server)} sent {ignored} replies, none of them to this question.");
		}
	}

	private static string DescribeFailure(SocketException ex, string endpoint) => ex.SocketErrorCode switch
	{
		// A connected UDP socket hears the ICMP port unreachable as a reset on Windows and as refused elsewhere.
		SocketError.ConnectionReset or SocketError.ConnectionRefused => $"Nothing answers DNS on {endpoint}.",
		SocketError.NetworkUnreachable or SocketError.HostUnreachable => $"There is no route to {endpoint}.",
		SocketError.AddressFamilyNotSupported or SocketError.AddressNotAvailable => $"This machine cannot reach {endpoint}: it has no address of that kind.",
		SocketError.TimedOut => $"{endpoint} did not answer in time.",
		_ => $"Asking {endpoint} failed ({ex.SocketErrorCode}).",
	};
}
