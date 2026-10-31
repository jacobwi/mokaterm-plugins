using System.Net;
using System.Net.NetworkInformation;
using Moka.Network;

namespace Moka.Network.Tests;

public sealed class LocalConnectionsTests
{
	[Fact]
	public void Describe_NamesEveryStateInWords()
	{
		Assert.Equal("Listening", LocalConnections.Describe(TcpState.Listen));
		Assert.Equal("Established", LocalConnections.Describe(TcpState.Established));
		Assert.Equal("Opening", LocalConnections.Describe(TcpState.SynSent));
		Assert.Equal("Opening", LocalConnections.Describe(TcpState.SynReceived));
		Assert.Equal("Closing", LocalConnections.Describe(TcpState.FinWait1));
		Assert.Equal("Closing, other end done", LocalConnections.Describe(TcpState.FinWait2));
		Assert.Equal("Closing, this end done", LocalConnections.Describe(TcpState.CloseWait));
		Assert.Equal("Waiting out the close", LocalConnections.Describe(TcpState.TimeWait));
		Assert.Equal("Closed", LocalConnections.Describe(TcpState.Closed));
		Assert.Equal("Unknown", LocalConnections.Describe((TcpState)99));
	}

	[Fact]
	public void Connections_MapsTheEndsAndTheState()
	{
		LocalConnectionRow row = Assert.Single(LocalConnections.Connections([new FakeConnection(
			new IPEndPoint(IPAddress.Parse("192.168.1.5"), 51234),
			new IPEndPoint(IPAddress.Parse("93.184.216.34"), 443),
			TcpState.Established)]));

		Assert.Equal("192.168.1.5:51234", row.Local);
		Assert.Equal("93.184.216.34:443", row.Remote);
		Assert.Equal("Established", row.State);
	}

	[Fact]
	public void Connections_FormatsIPv6WithBracketsAndThePortAfterTheColon()
	{
		LocalConnectionRow row = Assert.Single(LocalConnections.Connections([new FakeConnection(
			new IPEndPoint(IPAddress.IPv6Loopback, 51413),
			new IPEndPoint(IPAddress.Parse("2001:db8::1"), 443),
			TcpState.TimeWait)]));

		Assert.Equal("[::1]:51413", row.Local);
		Assert.Equal("[2001:db8::1]:443", row.Remote);
		Assert.Equal("Waiting out the close", row.State);
	}

	[Fact]
	public void Connections_GroupsByStateThenByLocalEnd()
	{
		FakeConnection[] connections =
		[
			new(new IPEndPoint(IPAddress.Loopback, 80), new IPEndPoint(IPAddress.Any, 0), TcpState.Established),
			new(new IPEndPoint(IPAddress.Loopback, 22), new IPEndPoint(IPAddress.Any, 0), TcpState.Listen),
			new(new IPEndPoint(IPAddress.Loopback, 25), new IPEndPoint(IPAddress.Any, 0), TcpState.Established),
		];

		IReadOnlyList<LocalConnectionRow> rows = LocalConnections.Connections(connections);

		Assert.Equal(["127.0.0.1:25", "127.0.0.1:80", "127.0.0.1:22"], rows.Select(row => row.Local));
		Assert.Equal(["Established", "Established", "Listening"], rows.Select(row => row.State));
	}

	[Fact]
	public void Listeners_ListsTcpThenUdpEachInAddressOrder()
	{
		IPEndPoint[] tcp =
		[
			new(IPAddress.IPv6Any, 80),
			new(IPAddress.Any, 22),
			new(IPAddress.Loopback, 443),
		];
		IPEndPoint[] udp =
		[
			new(IPAddress.Parse("2001:db8::1"), 53),
			new(IPAddress.Any, 5353),
		];

		IReadOnlyList<LocalListenerRow> rows = LocalConnections.Listeners(tcp, udp);

		Assert.Equal(5, rows.Count);
		Assert.Equal("tcp", rows[0].Protocol);
		Assert.Equal("udp", rows[3].Protocol);
		Assert.Equal("[::]:80", rows[0].Local);
		Assert.Equal("0.0.0.0:22", rows[1].Local);
		Assert.Equal("127.0.0.1:443", rows[2].Local);
		Assert.Equal("[2001:db8::1]:53", rows[3].Local);
		Assert.Equal("0.0.0.0:5353", rows[4].Local);
	}

	[Fact]
	public void Collect_ReadsThisMachineNow()
	{
		LocalTable table = LocalConnections.Collect();

		Assert.NotNull(table);
		Assert.All(table.Connections, row => Assert.NotEqual("", row.Local));
		Assert.All(table.Listeners, row => Assert.Contains(row.Protocol, ["tcp", "udp"], StringComparer.Ordinal));
	}

	/// <summary>The shape of what GetActiveTcpConnections hands over, filled with what a test wants.</summary>
	private sealed class FakeConnection(IPEndPoint local, IPEndPoint remote, TcpState state) : TcpConnectionInformation
	{
		public override IPEndPoint LocalEndPoint => local;

		public override IPEndPoint RemoteEndPoint => remote;

		public override TcpState State => state;
	}
}
