using System.Net;
using System.Net.Sockets;
using Moka.Network.Echo;

namespace Moka.Network.Tests.Echo;

/// <summary>The TCP pinger against servers on the loopback address, never the real network.</summary>
public sealed class TcpPingerTests
{
	[Fact]
	public async Task Send_ReportsTheConnectTimeWhenThePortAccepts()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		try
		{
			int port = ((IPEndPoint)listener.LocalEndpoint).Port;
			Task<TcpClient> serve = Task.Run(() => listener.AcceptTcpClientAsync(cancellationToken).AsTask(), cancellationToken);

			EchoReply reply = await new TcpPinger(port).SendAsync(IPAddress.Loopback, Request(), cancellationToken);
			using TcpClient accepted = await serve;

			Assert.Equal(EchoStatus.Reply, reply.Status);
			Assert.Equal(IPAddress.Loopback, reply.From);
			Assert.NotNull(reply.Milliseconds);
			Assert.Null(reply.Detail);
		}
		finally
		{
			listener.Stop();
		}
	}

	[Fact]
	public async Task Send_CountsARefusalAsAReplyFromTheHostItself()
	{
		TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();

		EchoReply reply = await new TcpPinger(port).SendAsync(IPAddress.Loopback, Request(), TestContext.Current.CancellationToken);

		Assert.Equal(EchoStatus.Reply, reply.Status);
		Assert.Equal(IPAddress.Loopback, reply.From);
		Assert.NotNull(reply.Milliseconds);
		Assert.Contains("closed", reply.Detail, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Send_WaitsNoLongerThanTheTimeoutForAnAddressThatNeverAnswers()
	{
		long started = TimeProvider.System.GetTimestamp();

		EchoReply reply = await new TcpPinger(81).SendAsync(IPAddress.Parse("192.0.2.1"), Request(TimeSpan.FromMilliseconds(300)), TestContext.Current.CancellationToken);

		// A dropped attempt and an unreachable answer are both fine here; a reply from TEST-NET is not.
		Assert.NotEqual(EchoStatus.Reply, reply.Status);
		Assert.True(TimeProvider.System.GetElapsedTime(started) < TimeSpan.FromSeconds(3));
	}

	[Fact]
	public async Task Send_StopsWhenCancelled()
	{
		using CancellationTokenSource stop = new();
		await stop.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => new TcpPinger(81).SendAsync(IPAddress.Parse("192.0.2.1"), Request(), stop.Token));
	}

	private static EchoRequest Request(TimeSpan? timeout = null) => new(EchoRequest.DefaultTtl, timeout ?? TimeSpan.FromSeconds(5), EchoRequest.DefaultPayload);
}
