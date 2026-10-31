using Moka.Network.Server;

namespace Moka.Network.Tests.Server;

/// <summary>
/// Packet lines as tcpdump -nn -l writes them, the shapes the capture tab really meets: a TCP handshake, an IPv6
/// data packet, UDP, DNS with its transaction id, ICMP in both spellings, ARP without ends, and a line that is
/// nothing of the sort, which the parser keeps whole rather than losing.
/// </summary>
public sealed class TcpDumpLineTests
{
	public static TheoryData<string, string, string, string, string, string, string> Lines => new()
	{
		// line, time, source, destination, protocol, length, info
		{
			"14:02:11.394502 IP 10.0.0.5.52344 > 93.184.216.34.443: Flags [S], seq 123456789, win 64240, options [mss 1460,sackOK,TS val 123 ecr 0,nop,wscale 7], length 0",
			"14:02:11.394502", "10.0.0.5:52344", "93.184.216.34:443", "TCP", "0", "Flags [S], seq 123456789, win 64240, options [mss 1460,sackOK,TS val 123 ecr 0,nop,wscale 7], length 0"
		},
		{
			"14:02:11.412884 IP 93.184.216.34.443 > 10.0.0.5.52344: Flags [S.], seq 987654321, ack 123456790, win 62643, length 0",
			"14:02:11.412884", "93.184.216.34:443", "10.0.0.5:52344", "TCP", "0", "Flags [S.], seq 987654321, ack 123456790, win 62643, length 0"
		},
		{
			"14:02:12.001123 IP6 2001:db8::1.443 > 2001:db8::2.52344: Flags [P.], seq 1:531, ack 1, win 65535, length 530",
			"14:02:12.001123", "[2001:db8::1]:443", "[2001:db8::2]:52344", "TCP", "530", "Flags [P.], seq 1:531, ack 1, win 65535, length 530"
		},
		{
			"14:03:01.998877 IP 10.0.0.2.5353 > 10.0.0.1.5353: UDP, length 62",
			"14:03:01.998877", "10.0.0.2:5353", "10.0.0.1:5353", "UDP", "62", "UDP, length 62"
		},
		{
			"14:03:02.000001 IP6 2001:db8::8.53 > 2001:db8::2.49152: 51234 A? example.com. (30)",
			"14:03:02.000001", "[2001:db8::8]:53", "[2001:db8::2]:49152", "", "", "51234 A? example.com. (30)"
		},
		{
			"14:03:05.000000 IP 10.0.0.5 > 93.184.216.34: ICMP echo request, id 9, seq 1, length 64",
			"14:03:05.000000", "10.0.0.5", "93.184.216.34", "ICMP", "64", "ICMP echo request, id 9, seq 1, length 64"
		},
		{
			"14:03:06.100000 IP6 fe80::1 > ff02::1a: ICMP6, router advertisement, length 64",
			"14:03:06.100000", "fe80::1", "ff02::1a", "ICMP6", "64", "ICMP6, router advertisement, length 64"
		},
		{
			"14:03:07.000000 IP 10.0.0.1 > 10.0.0.2: icmp 64: echo request seq 1",
			"14:03:07.000000", "10.0.0.1", "10.0.0.2", "ICMP", "64", "icmp 64: echo request seq 1"
		},
		{
			"14:04:00.000123 ARP, Request who-has 10.0.0.1 (aa:bb:cc:dd:ee:ff) tell 10.0.0.2, length 46",
			"14:04:00.000123", "", "", "ARP", "", "Request who-has 10.0.0.1 (aa:bb:cc:dd:ee:ff) tell 10.0.0.2, length 46"
		},
		{
			"14:05:00.000000 IP 10.0.0.9.49152 > 239.255.255.250.1900: UDP, length 286",
			"14:05:00.000000", "10.0.0.9:49152", "239.255.255.250:1900", "UDP", "286", "UDP, length 286"
		},
	};

	[Theory]
	[MemberData(nameof(Lines))]
	public void Parse_SplitsALineIntoItsColumns(string line, string time, string source, string destination, string protocol, string length, string info)
	{
		TcpDumpLine parsed = TcpDumpLine.Parse(line);

		Assert.Equal(time, parsed.Time);
		Assert.Equal(source, parsed.Source);
		Assert.Equal(destination, parsed.Destination);
		Assert.Equal(protocol, parsed.Protocol);
		Assert.Equal(length, parsed.Length);
		Assert.Equal(info, parsed.Info);
	}

	[Fact]
	public void Parse_KeepsALineThatIsNothingOfTheSort()
	{
		TcpDumpLine parsed = TcpDumpLine.Parse("listening on eth0, link-type EN10MB (Ethernet), snapshot length 262144 bytes");

		Assert.Equal("", parsed.Time);
		Assert.Equal("", parsed.Source);
		Assert.Equal("", parsed.Destination);
		Assert.Equal("listening", parsed.Protocol);
		Assert.StartsWith("on eth0", parsed.Info, StringComparison.Ordinal);
	}

	[Fact]
	public void Parse_AnEmptyLineIsAnEmptyRow()
	{
		TcpDumpLine parsed = TcpDumpLine.Parse("   ");

		Assert.Equal(new TcpDumpLine(), parsed);
	}

	[Fact]
	public void Parse_LeavesAnEndItCannotNameAlone()
	{
		TcpDumpLine parsed = TcpDumpLine.Parse("14:06:00.000000 IP weird %thing > stranger: something odd");

		Assert.Equal("weird %thing", parsed.Source);
		Assert.Equal("stranger", parsed.Destination);
	}

	[Fact]
	public void Endpoint_BracketsIPv6WithItsPortAndLeavesABareAddressBare()
	{
		Assert.Equal("[2001:db8::1]:443", TcpDumpLine.Endpoint("2001:db8::1.443"));
		Assert.Equal("2001:db8::1", TcpDumpLine.Endpoint("2001:db8::1"));
		Assert.Equal("10.0.0.5:52344", TcpDumpLine.Endpoint("10.0.0.5.52344"));
		Assert.Equal("10.0.0.5", TcpDumpLine.Endpoint("10.0.0.5"));
	}
}
