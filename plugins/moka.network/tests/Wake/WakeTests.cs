using System.Net;
using System.Net.Sockets;
using Moka.Network.Wake;

namespace Moka.Network.Tests.Wake;

public sealed class WakeTests
{
	[Theory]
	[InlineData("00:11:22:aa:bb:cc")]
	[InlineData("00-11-22-AA-BB-CC")]
	[InlineData("0011.22aa.bbcc")]
	[InlineData("001122AABBCC")]
	[InlineData("  00:11:22:Aa:bB:cc  ")]
	public void Mac_ReadsEveryCommonForm(string text)
	{
		Assert.True(MacAddress.TryParse(text, out MacAddress? mac, out string? problem), problem);
		Assert.Equal("00:11:22:AA:BB:CC", mac.ToString());
		Assert.Equal(new byte[] { 0x00, 0x11, 0x22, 0xAA, 0xBB, 0xCC }, mac.Bytes.ToArray());
	}

	[Fact]
	public void Mac_ReadsShortGroups()
	{
		Assert.True(MacAddress.TryParse("a:b:c:d:e:f", out MacAddress? mac, out _));
		Assert.Equal("0A:0B:0C:0D:0E:0F", mac.ToString());
	}

	[Theory]
	[InlineData("")]
	[InlineData("00:11:22:33:44")]
	[InlineData("00:11:22:33:44:55:66")]
	[InlineData("00:11:22:33:44:5g")]
	[InlineData("00:11-22:33:44:55")]
	[InlineData("0011.2233.445")]
	[InlineData("00112233445")]
	[InlineData("001:1:22:33:44:55")]
	[InlineData("00:11::22:33:44")]
	[InlineData("+0:11:22:33:44:55")]
	public void Mac_RefusesWhatIsNotOne(string text)
	{
		Assert.False(MacAddress.TryParse(text, out _, out string? problem));
		Assert.False(string.IsNullOrEmpty(problem));
	}

	[Theory]
	[InlineData("ff:ff:ff:ff:ff:ff", "broadcast")]
	[InlineData("00:00:00:00:00:00", "not a network card")]
	[InlineData("01:00:5e:00:00:fb", "multicast")]
	public void Mac_RefusesAddressesNoCardHas(string text, string reason)
	{
		Assert.False(MacAddress.TryParse(text, out _, out string? problem));
		Assert.Contains(reason, problem, StringComparison.Ordinal);
	}

	[Fact]
	public void MagicPacket_IsSixFfThenTheAddressSixteenTimes()
	{
		Assert.True(MacAddress.TryParse("00:11:22:AA:BB:CC", out MacAddress? mac, out _));

		byte[] packet = MagicPacket.Build(mac);

		Assert.Equal(102, packet.Length);
		Assert.Equal(MagicPacket.Size, packet.Length);
		Assert.All(packet[..6], b => Assert.Equal(0xFF, b));
		for (int copy = 0; copy < 16; copy++)
		{
			Assert.Equal(mac.Bytes.ToArray(), packet.AsSpan(6 + (copy * 6), 6).ToArray());
		}
	}

	[Fact]
	public void MagicPacket_HexShowsSixBytesALine()
	{
		Assert.True(MacAddress.TryParse("00:11:22:AA:BB:CC", out MacAddress? mac, out _));

		string[] lines = MagicPacket.Hex(MagicPacket.Build(mac)).Split('\n');

		Assert.Equal(17, lines.Length);
		Assert.Equal("FF FF FF FF FF FF", lines[0]);
		Assert.All(lines[1..], line => Assert.Equal("00 11 22 AA BB CC", line));
	}

	[Theory]
	[InlineData("", "255.255.255.255")]
	[InlineData("192.168.1.255", "192.168.1.255")]
	[InlineData("192.168.1.0/24", "192.168.1.255")]
	[InlineData("10.20.30.40/255.255.0.0", "10.20.255.255")]
	[InlineData("192.168.1.20", "192.168.1.20")]
	public void Target_ReadsABroadcastAnAddressOrAPrefix(string text, string expected)
	{
		Assert.True(WakeTarget.TryParse(text, out IPAddress? address, out string? problem), problem);
		Assert.Equal(IPAddress.Parse(expected), address);
	}

	[Theory]
	[InlineData("10.0.0.0/31", "no broadcast")]
	[InlineData("2001:db8::/64", "IPv4")]
	[InlineData("2001:db8::1", "not an IPv4")]
	[InlineData("10.1", "not an IPv4")]
	[InlineData("example.com", "not an IPv4")]
	[InlineData("224.0.0.1", "cannot carry")]
	[InlineData("0.0.0.0", "cannot carry")]
	public void Target_RefusesWhereAPacketCannotGo(string text, string reason)
	{
		Assert.False(WakeTarget.TryParse(text, out _, out string? problem));
		Assert.Contains(reason, problem, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Send_PutsThePacketOnTheWire()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		using Socket receiver = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
		receiver.Bind(new IPEndPoint(IPAddress.Loopback, 0));
		Assert.True(MacAddress.TryParse("00:11:22:AA:BB:CC", out MacAddress? mac, out _));
		byte[] packet = MagicPacket.Build(mac);

		int sent = await WakeSender.SendAsync(packet, (IPEndPoint)receiver.LocalEndPoint!, cancellationToken);
		byte[] buffer = new byte[512];
		int received = await receiver.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);

		Assert.Equal(102, sent);
		Assert.Equal(packet, buffer[..received]);
	}
}
