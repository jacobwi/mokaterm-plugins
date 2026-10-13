using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Moka.Network.Lookup;

namespace Moka.Network.Tests.Lookup;

/// <summary>The DNS client against servers on the loopback address that answer the way the test needs, never the real network.</summary>
public sealed class DnsClientTests
{
	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

	[Fact]
	public async Task Exchange_PassesOverStrayRepliesAndTakesTheAnswer()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		using Socket server = UdpServer(out IPEndPoint endpoint);
		Task serve = Task.Run(
			async () =>
			{
				(DnsMessage query, EndPoint client) = await ReceiveAsync(server, cancellationToken);
				ushort id = query.Header.Id;
				await server.SendToAsync(Reply(id ^ 1, "example.com", 1), client, cancellationToken);
				await server.SendToAsync(Reply(id, "example.org", 1), client, cancellationToken);
				await server.SendToAsync(Reply(id, "example.com", 1, answers: [10, 0, 0, 1]), client, cancellationToken);
			},
			cancellationToken);

		DnsExchange exchange = await DnsClient.ExchangeAsync(endpoint, "example.com", 1, Limit, cancellationToken);
		await serve;

		Assert.Equal("10.0.0.1", Assert.Single(exchange.Message.Answers).Data);
		Assert.Equal(2, exchange.IgnoredReplies);
		Assert.False(exchange.OverTcp);
		Assert.Equal(endpoint, exchange.Server);
	}

	[Fact]
	public async Task Exchange_AsksAgainOverTcpWhenTheUdpAnswerIsCut()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		(Socket udpServer, TcpListener listener) = PairedServers();
		using Socket server = udpServer;
		IPEndPoint endpoint = (IPEndPoint)server.LocalEndPoint!;
		try
		{
			Task udp = Task.Run(
				async () =>
				{
					(DnsMessage query, EndPoint client) = await ReceiveAsync(server, cancellationToken);
					await server.SendToAsync(Reply(query.Header.Id, "example.com", 16, flags: 0x8380), client, cancellationToken);
				},
				cancellationToken);

			Task tcp = Task.Run(
				async () =>
				{
					using TcpClient accepted = await listener.AcceptTcpClientAsync(cancellationToken);
					NetworkStream stream = accepted.GetStream();
					byte[] prefix = new byte[2];
					await stream.ReadExactlyAsync(prefix, cancellationToken);
					byte[] query = new byte[BinaryPrimitives.ReadUInt16BigEndian(prefix)];
					await stream.ReadExactlyAsync(query, cancellationToken);
					DnsMessage parsed = DnsMessageParser.Parse(query);
					byte[] reply = Reply(parsed.Header.Id, "example.com", 16, answers: [5, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o']);
					byte[] framed = new byte[reply.Length + 2];
					BinaryPrimitives.WriteUInt16BigEndian(framed, (ushort)reply.Length);
					reply.CopyTo(framed, 2);
					await stream.WriteAsync(framed, cancellationToken);
				},
				cancellationToken);

			DnsExchange exchange = await DnsClient.ExchangeAsync(endpoint, "example.com", 16, Limit, cancellationToken);
			await Task.WhenAll(udp, tcp);

			Assert.True(exchange.OverTcp);
			Assert.False(exchange.Message.Header.Truncated);
			Assert.Equal("\"hello\"", Assert.Single(exchange.Message.Answers).Data);
		}
		finally
		{
			listener.Stop();
		}
	}

	[Fact]
	public async Task Exchange_SendsTheQueryAgainHalfwayAndGivesUpAtTheLimit()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		using Socket server = UdpServer(out IPEndPoint endpoint);
		int received = 0;
		using CancellationTokenSource listen = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		Task serve = Task.Run(
			async () =>
			{
				try
				{
					while (true)
					{
						await ReceiveAsync(server, listen.Token);
						Interlocked.Increment(ref received);
					}
				}
				catch (OperationCanceledException)
				{
					// The test is done listening.
				}
			},
			cancellationToken);

		DnsLookupException error = await Assert.ThrowsAsync<DnsLookupException>(
			() => DnsClient.ExchangeAsync(endpoint, "example.com", 1, TimeSpan.FromMilliseconds(600), cancellationToken));
		await listen.CancelAsync();
		await serve;

		Assert.Contains("did not answer", error.Message, StringComparison.Ordinal);
		Assert.Equal(2, Volatile.Read(ref received));
	}

	[Fact]
	public async Task Exchange_ReportsAnAnswerThatCannotBeRead()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		using Socket server = UdpServer(out IPEndPoint endpoint);
		Task serve = Task.Run(
			async () =>
			{
				(DnsMessage query, EndPoint client) = await ReceiveAsync(server, cancellationToken);
				byte[] broken = new Packet().Header(query.Header.Id, 0x8180, 1, 2).Question("example.com", 1).ToArray();
				await server.SendToAsync(broken, client, cancellationToken);
			},
			cancellationToken);

		DnsLookupException error = await Assert.ThrowsAsync<DnsLookupException>(
			() => DnsClient.ExchangeAsync(endpoint, "example.com", 1, Limit, cancellationToken));
		await serve;

		Assert.Contains("could not be read", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Exchange_SaysWhenNothingListensOnThePort()
	{
		IPEndPoint closed;
		using (Socket probe = UdpServer(out closed))
		{
			// Bound and closed again: the port is free, so the loopback answers with port unreachable.
		}

		DnsLookupException error = await Assert.ThrowsAsync<DnsLookupException>(
			() => DnsClient.ExchangeAsync(closed, "example.com", 1, Limit, TestContext.Current.CancellationToken));

		Assert.True(
			error.Message.Contains("Nothing answers DNS", StringComparison.Ordinal) || error.Message.Contains("did not answer", StringComparison.Ordinal),
			error.Message);
	}

	[Fact]
	public async Task Lookup_TriesTheNextServerWhenOneDoesNotAnswer()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		using Socket silent = UdpServer(out IPEndPoint first);
		using Socket server = UdpServer(out IPEndPoint second);
		Task serve = Task.Run(
			async () =>
			{
				(DnsMessage query, EndPoint client) = await ReceiveAsync(server, cancellationToken);
				await server.SendToAsync(Reply(query.Header.Id, "example.com", 1, flags: 0x8183), client, cancellationToken);
			},
			cancellationToken);

		DnsLookupResult result = await DnsLookup.RunAsync([first, second], "example.com", 1, TimeSpan.FromMilliseconds(400), cancellationToken);
		await serve;

		Assert.Equal(second, result.Exchange.Server);
		Assert.Equal("NXDOMAIN", result.Exchange.Message.ResponseCodeName);
		Assert.Contains("did not answer", Assert.Single(result.EarlierFailures), StringComparison.Ordinal);
	}

	/// <summary>
	/// A UDP socket and a TCP listener on the same loopback port, as one DNS server listens on both. Windows keeps some
	/// port ranges back per protocol, so a port free for TCP may still be refused for UDP; another one is tried then.
	/// </summary>
	private static (Socket Udp, TcpListener Tcp) PairedServers()
	{
		for (int attempt = 0; ; attempt++)
		{
			TcpListener listener = new(IPAddress.Loopback, 0);
			listener.Start();
			int port = ((IPEndPoint)listener.LocalEndpoint).Port;
			Socket udp = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			try
			{
				udp.Bind(new IPEndPoint(IPAddress.Loopback, port));
				return (udp, listener);
			}
			catch (SocketException) when (attempt < 20)
			{
				udp.Dispose();
				listener.Stop();
			}
		}
	}

	private static Socket UdpServer(out IPEndPoint endpoint)
	{
		Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
		socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
		endpoint = (IPEndPoint)socket.LocalEndPoint!;
		return socket;
	}

	private static async Task<(DnsMessage Query, EndPoint Client)> ReceiveAsync(Socket server, CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[2048];
		SocketReceiveFromResult received = await server.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), cancellationToken);
		return (DnsMessageParser.Parse(buffer, received.ReceivedBytes), received.RemoteEndPoint);
	}

	/// <summary>A reply with one question and, when data is given, one answer of the question's type pointing at its name.</summary>
	private static byte[] Reply(int id, string name, int type, int flags = 0x8180, byte[]? answers = null)
	{
		Packet packet = new Packet().Header(id, flags, 1, answers is null ? 0 : 1).Question(name, type);
		if (answers is not null)
		{
			packet.Pointer(12).Fixed(type, 60, answers);
		}

		return packet.ToArray();
	}
}
