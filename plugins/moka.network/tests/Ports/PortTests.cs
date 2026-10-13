using System.Net;
using System.Net.Sockets;
using Moka.Network.Ports;

namespace Moka.Network.Tests.Ports;

public sealed class PortTests
{
	[Theory]
	[InlineData("22", new[] { 22 })]
	[InlineData("22, 80,443", new[] { 22, 80, 443 })]
	[InlineData("ssh https RDP", new[] { 22, 443, 3389 })]
	[InlineData("22;22 22", new[] { 22 })]
	[InlineData("1 2 3 4 5 6 7 8 9 10", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 })]
	public void List_ReadsNumbersAndServiceNames(string text, int[] expected)
	{
		Assert.True(PortList.TryParse(text, out IReadOnlyList<int>? ports, out string? problem), problem);
		Assert.Equal(expected, ports);
	}

	[Theory]
	[InlineData("", "Type a port")]
	[InlineData("1-1024", "No ranges")]
	[InlineData("20:25", "No ranges")]
	[InlineData("1 2 3 4 5 6 7 8 9 10 11", "At most 10")]
	[InlineData("0", "not a port")]
	[InlineData("65536", "not a port")]
	[InlineData("+22", "not a port")]
	[InlineData("gopher", "not a port")]
	public void List_RefusesRangesLongListsAndNonsense(string text, string expected)
	{
		Assert.False(PortList.TryParse(text, out _, out string? problem));
		Assert.Contains(expected, problem, StringComparison.Ordinal);
	}

	[Fact]
	public void List_NamesTheUsualService()
	{
		Assert.Equal("ssh", PortList.ServiceName(22));
		Assert.Null(PortList.ServiceName(31337));
	}

	[Fact]
	public async Task Check_ReadsTheGreetingAsPlainText()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		try
		{
			int port = ((IPEndPoint)listener.LocalEndpoint).Port;
			Task serve = Task.Run(
				async () =>
				{
					using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
					await client.GetStream().WriteAsync("SSH-2.0-Test\r\n\u001b[31mred\u0007\r\n"u8.ToArray(), cancellationToken);
					await Task.Delay(1500, cancellationToken);
				},
				cancellationToken);

			PortResult result = await PortCheck.CheckAsync(IPAddress.Loopback, port, TimeSpan.FromSeconds(5), readGreeting: true, cancellationToken);
			await serve;

			Assert.Equal(PortState.Open, result.State);
			Assert.Equal(new IPEndPoint(IPAddress.Loopback, port), result.Endpoint);
			Assert.Equal("SSH-2.0-Test\nred\n", result.Greeting);
			Assert.Equal(25, result.GreetingBytes);
		}
		finally
		{
			listener.Stop();
		}
	}

	[Fact]
	public async Task Check_KeepsNoMoreThanTheGreetingCap()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		try
		{
			int port = ((IPEndPoint)listener.LocalEndpoint).Port;
			Task serve = Task.Run(
				async () =>
				{
					using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
					byte[] flood = new byte[64 * 1024];
					Array.Fill(flood, (byte)'x');
					try
					{
						await client.GetStream().WriteAsync(flood, cancellationToken);
					}
					catch (IOException)
					{
						// The check hung up after its 512 bytes, which is the point.
					}
				},
				cancellationToken);

			PortResult result = await PortCheck.CheckAsync(IPAddress.Loopback, port, TimeSpan.FromSeconds(5), readGreeting: true, cancellationToken);
			await serve;

			Assert.Equal(PortCheck.MaxGreetingBytes, result.GreetingBytes);
			Assert.Equal(PortCheck.MaxGreetingBytes, result.Greeting?.Length);
		}
		finally
		{
			listener.Stop();
		}
	}

	[Fact]
	public async Task Check_WaitsNoLongerThanASecondForAServerThatSaysNothing()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		try
		{
			int port = ((IPEndPoint)listener.LocalEndpoint).Port;
			Task<TcpClient> accept = listener.AcceptTcpClientAsync(cancellationToken).AsTask();
			long started = TimeProvider.System.GetTimestamp();

			PortResult result = await PortCheck.CheckAsync(IPAddress.Loopback, port, TimeSpan.FromSeconds(5), readGreeting: true, cancellationToken);
			using TcpClient accepted = await accept;

			Assert.Equal(PortState.Open, result.State);
			Assert.Null(result.Greeting);
			Assert.True(TimeProvider.System.GetElapsedTime(started) < TimeSpan.FromSeconds(3));
		}
		finally
		{
			listener.Stop();
		}
	}

	[Fact]
	public async Task Check_SaysRefusedWhenNothingListens()
	{
		TcpListener listener = new(IPAddress.Loopback, 0);
		listener.Start();
		int port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();

		PortResult result = await PortCheck.CheckAsync(IPAddress.Loopback, port, TimeSpan.FromSeconds(10), readGreeting: false, TestContext.Current.CancellationToken);

		Assert.Equal(PortState.Refused, result.State);
		Assert.Null(result.Greeting);
	}

	[Fact]
	public async Task Check_StopsWhenCancelled()
	{
		using CancellationTokenSource stop = new();
		await stop.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => PortCheck.CheckAsync(IPAddress.Parse("192.0.2.1"), 80, TimeSpan.FromSeconds(5), readGreeting: false, stop.Token));
	}
}
