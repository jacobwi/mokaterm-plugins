using System.Net;
using System.Numerics;
using Moka.Network.Subnets;

namespace Moka.Network.Tests.Subnets;

public sealed class SubnetMathTests
{
	[Fact]
	public void Calculate_WorksOutAnIPv4Prefix()
	{
		SubnetInfo info = Calculate("192.168.1.10/24");

		Assert.Equal("192.168.1.0/24", info.Cidr);
		Assert.Equal(IPAddress.Parse("255.255.255.0"), info.Mask);
		Assert.Equal(IPAddress.Parse("0.0.0.255"), info.Wildcard);
		Assert.Equal(IPAddress.Parse("192.168.1.255"), info.Broadcast);
		Assert.Equal(IPAddress.Parse("192.168.1.1"), info.FirstHost);
		Assert.Equal(IPAddress.Parse("192.168.1.254"), info.LastHost);
		Assert.Equal(254, info.HostCount);
		Assert.Equal(256, info.AddressCount);
		Assert.Equal(32, info.Bits);
	}

	[Theory]
	[InlineData("10.0.0.0/255.0.0.0", 8)]
	[InlineData("10.0.0.0/0.0.0.0", 0)]
	[InlineData("10.0.0.7", 32)]
	[InlineData("2001:db8::1", 128)]
	public void Calculate_ReadsAMaskOrNoPrefix(string text, int prefix) => Assert.Equal(prefix, Calculate(text).PrefixLength);

	[Fact]
	public void Calculate_GivesBothAddressesOfASlash31ToHosts()
	{
		SubnetInfo pair = Calculate("10.0.0.0/31");
		SubnetInfo single = Calculate("10.0.0.9/32");

		Assert.Null(pair.Broadcast);
		Assert.Equal(2, pair.HostCount);
		Assert.Equal(IPAddress.Parse("10.0.0.0"), pair.FirstHost);
		Assert.Equal(IPAddress.Parse("10.0.0.1"), pair.LastHost);
		Assert.Equal(1, single.HostCount);
		Assert.Equal(IPAddress.Parse("10.0.0.9"), single.FirstHost);
	}

	[Fact]
	public void Calculate_CoversTheWholeIPv4Space()
	{
		SubnetInfo all = Calculate("0.0.0.0/0");

		Assert.Equal(new BigInteger(uint.MaxValue) + 1, all.AddressCount);
		Assert.Equal(new BigInteger(uint.MaxValue) - 1, all.HostCount);
		Assert.Equal(IPAddress.Parse("255.255.255.255"), all.Broadcast);
	}

	[Fact]
	public void Calculate_WorksOutAnIPv6PrefixWithoutABroadcast()
	{
		SubnetInfo info = Calculate("2001:db8:abcd:12::1/64");

		Assert.Equal("2001:db8:abcd:12::/64", info.Cidr);
		Assert.Equal(IPAddress.Parse("ffff:ffff:ffff:ffff::"), info.Mask);
		Assert.Null(info.Broadcast);
		Assert.Null(info.Wildcard);
		Assert.Equal(IPAddress.Parse("2001:db8:abcd:12::"), info.FirstHost);
		Assert.Equal(IPAddress.Parse("2001:db8:abcd:12:ffff:ffff:ffff:ffff"), info.LastHost);
		Assert.Equal(BigInteger.One << 64, info.HostCount);
		Assert.True(info.IsIPv6);
	}

	[Fact]
	public void Calculate_LeavesAnIPv6ZoneOut()
	{
		SubnetInfo info = Calculate("fe80::1%5/64");

		Assert.Equal(0, info.Address.ScopeId);
		Assert.Equal("fe80::/64", info.Cidr);
	}

	[Theory]
	[InlineData("", "Type an address")]
	[InlineData("10.1/8", "not an IPv4 or IPv6 address")]
	[InlineData("/24", "not an IPv4 or IPv6 address")]
	[InlineData("10.0.0.0/33", "0 to 32")]
	[InlineData("2001:db8::/129", "0 to 128")]
	[InlineData("10.0.0.0/255.0.255.0", "not a mask")]
	[InlineData("2001:db8::/ffff::", "not a prefix length")]
	[InlineData("10.0.0.0/x", "not a prefix length")]
	public void TryCalculate_SaysWhyItCannotRead(string text, string expected)
	{
		Assert.False(SubnetMath.TryCalculate(text, out _, out string? problem));
		Assert.Contains(expected, problem, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("192.168.1.0/24", "192.168.1.200", true)]
	[InlineData("192.168.1.0/24", "192.168.2.1", false)]
	[InlineData("192.168.1.0/24", "::ffff:192.168.1.5", true)]
	[InlineData("192.168.1.0/24", "2001:db8::1", false)]
	[InlineData("2001:db8::/32", "2001:db8:ffff::1", true)]
	[InlineData("2001:db8::/32", "2001:db9::1", false)]
	[InlineData("2001:db8::/32", "192.168.1.1", false)]
	[InlineData("0.0.0.0/0", "8.8.8.8", true)]
	public void Contains_ComparesTheNetworkBits(string subnet, string address, bool expected) =>
		Assert.Equal(expected, SubnetMath.Contains(Calculate(subnet), IPAddress.Parse(address)));

	[Fact]
	public void Split_ByCountRoundsUpToAPowerOfTwo()
	{
		SubnetInfo info = Calculate("192.168.1.0/24");

		Assert.True(SubnetMath.TryParseSplit("3", info, out int prefix, out _));
		IReadOnlyList<SubnetInfo> parts = SubnetMath.Split(info, prefix, SubnetMath.MaxSplitRows, out BigInteger total);

		Assert.Equal(26, prefix);
		Assert.Equal(4, total);
		Assert.Equal(["192.168.1.0/26", "192.168.1.64/26", "192.168.1.128/26", "192.168.1.192/26"], parts.Select(part => part.Cidr));
		Assert.All(parts, part => Assert.Equal(62, part.HostCount));
	}

	[Theory]
	[InlineData("2", 25)]
	[InlineData("4", 26)]
	[InlineData("5", 27)]
	[InlineData("/30", 30)]
	public void TryParseSplit_ReadsACountOrAPrefix(string text, int expected)
	{
		Assert.True(SubnetMath.TryParseSplit(text, Calculate("10.0.0.0/24"), out int prefix, out string? problem), problem);
		Assert.Equal(expected, prefix);
	}

	[Theory]
	[InlineData("1")]
	[InlineData("0")]
	[InlineData("/24")]
	[InlineData("/20")]
	[InlineData("/33")]
	[InlineData("512")]
	[InlineData("four")]
	public void TryParseSplit_RefusesWhatCannotBeDone(string text) =>
		Assert.False(SubnetMath.TryParseSplit(text, Calculate("10.0.0.0/24"), out _, out _));

	[Fact]
	public void Split_ListsNoMoreThanTheCapButCountsThemAll()
	{
		SubnetInfo info = Calculate("2001:db8::/48");

		IReadOnlyList<SubnetInfo> parts = SubnetMath.Split(info, 64, SubnetMath.MaxSplitRows, out BigInteger total);

		Assert.Equal(65536, total);
		Assert.Equal(SubnetMath.MaxSplitRows, parts.Count);
		Assert.Equal("2001:db8::/64", parts[0].Cidr);
		Assert.Equal("2001:db8:0:ff::/64", parts[^1].Cidr);
	}

	[Fact]
	public void Expanded_WritesEveryGroupInFull() =>
		Assert.Equal("2001:0db8:0000:0000:0000:0000:0000:0001", SubnetMath.Expanded(IPAddress.Parse("2001:db8::1")));

	[Fact]
	public void Binary_WritesTheMaskByOctet() =>
		Assert.Equal("11111111.11111111.11110000.00000000", SubnetMath.Binary(IPAddress.Parse("255.255.240.0")));

	[Theory]
	[InlineData("10.1.2.3", "Private", false)]
	[InlineData("172.31.255.1", "Private", false)]
	[InlineData("172.32.0.1", "Public", true)]
	[InlineData("192.168.0.1", "Private", false)]
	[InlineData("100.64.1.1", "Shared (CGNAT)", false)]
	[InlineData("127.0.0.1", "Loopback", false)]
	[InlineData("169.254.10.1", "Link-local", false)]
	[InlineData("192.0.2.5", "Documentation", false)]
	[InlineData("192.0.0.8", "IETF protocol assignments", false)]
	[InlineData("198.18.0.1", "Benchmarking", false)]
	[InlineData("224.0.0.251", "Multicast", false)]
	[InlineData("240.0.0.1", "Reserved", false)]
	[InlineData("255.255.255.255", "Limited broadcast", false)]
	[InlineData("8.8.8.8", "Public", true)]
	[InlineData("::1", "Loopback", false)]
	[InlineData("::", "Unspecified", false)]
	[InlineData("fe80::1", "Link-local", false)]
	[InlineData("fd12:3456::1", "Unique local", false)]
	[InlineData("2001:db8::1", "Documentation", false)]
	[InlineData("3fff::1", "Documentation", false)]
	[InlineData("ff02::1", "Multicast", false)]
	[InlineData("2606:4700:4700::1111", "Global unicast", true)]
	[InlineData("::ffff:10.0.0.1", "IPv4-mapped", false)]
	[InlineData("4000::1", "Reserved", false)]
	public void Kinds_SortAnAddressIntoItsMostSpecificRange(string address, string kind, bool isPublic)
	{
		AddressKind found = AddressKinds.Classify(IPAddress.Parse(address));

		Assert.Equal(kind, found.Name);
		Assert.Equal(isPublic, found.IsPublic);
	}

	private static SubnetInfo Calculate(string text)
	{
		Assert.True(SubnetMath.TryCalculate(text, out SubnetInfo? info, out string? problem), problem);
		return info;
	}
}
