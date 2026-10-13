using System.Net;
using Moka.Network.Lookup;

namespace Moka.Network.Tests.Lookup;

public sealed class DnsNameTests
{
	[Theory]
	[InlineData("Example.COM.", "example.com")]
	[InlineData("  example.com  ", "example.com")]
	[InlineData(".", ".")]
	[InlineData("bücher.de", "xn--bcher-kva.de")]
	[InlineData("_sip._tcp.example.com", "_sip._tcp.example.com")]
	[InlineData("*.example.com", "*.example.com")]
	[InlineData("1.2.0.192.in-addr.arpa", "1.2.0.192.in-addr.arpa")]
	public void TryNormalize_ReadsWhatPeopleType(string text, string expected)
	{
		Assert.True(DnsName.TryNormalize(text, out string? name, out string? problem), problem);
		Assert.Equal(expected, name);
	}

	[Theory]
	[InlineData("")]
	[InlineData("a..b")]
	[InlineData(".example.com")]
	[InlineData("exa mple.com")]
	[InlineData("exa$mple.com")]
	[InlineData("ex*ample.com")]
	[InlineData("tab\there")]
	public void TryNormalize_RefusesWhatIsNotAName(string text)
	{
		Assert.False(DnsName.TryNormalize(text, out _, out string? problem));
		Assert.False(string.IsNullOrEmpty(problem));
	}

	[Fact]
	public void TryNormalize_HoldsLabelsAndNamesToTheirLimits()
	{
		string longest = new('a', 63);
		Assert.True(DnsName.TryNormalize(longest + ".com", out _, out _));
		Assert.False(DnsName.TryNormalize(longest + "a.com", out _, out string? label));
		Assert.Contains("63", label, StringComparison.Ordinal);

		string four = string.Join('.', longest, longest, longest, new string('b', 61));
		Assert.True(DnsName.TryNormalize(four, out _, out _));
		Assert.False(DnsName.TryNormalize(four + "b", out _, out string? name));
		Assert.Contains("255", name, StringComparison.Ordinal);
	}

	[Fact]
	public void Encode_WritesLengthsAndTheFinalZero()
	{
		Assert.Equal([1, (byte)'a', 2, (byte)'b', (byte)'c', 0], DnsName.Encode("a.bc"));
		Assert.Equal([0], DnsName.Encode(DnsName.Root));
	}

	[Theory]
	[InlineData("192.0.2.1", "1.2.0.192.in-addr.arpa")]
	[InlineData("::ffff:192.0.2.1", "1.2.0.192.in-addr.arpa")]
	[InlineData("2001:db8::1", "1.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.0.8.b.d.0.1.0.0.2.ip6.arpa")]
	public void ReverseName_ReversesOctetsOrNibbles(string address, string expected) =>
		Assert.Equal(expected, ReverseName.For(IPAddress.Parse(address)));

	[Theory]
	[InlineData("1.1.1.1", "1.1.1.1", 53)]
	[InlineData("192.168.1.1:5353", "192.168.1.1", 5353)]
	[InlineData("2001:db8::53", "2001:db8::53", 53)]
	[InlineData("[2001:db8::53]:5353", "2001:db8::53", 5353)]
	[InlineData("[2001:db8::53]", "2001:db8::53", 53)]
	public void ServerAddress_ReadsAnAddressAndAnOptionalPort(string text, string address, int port)
	{
		Assert.True(DnsServerAddress.TryParse(text, out IPEndPoint? endpoint, out string? problem), problem);
		Assert.Equal(new IPEndPoint(IPAddress.Parse(address), port), endpoint);
	}

	[Theory]
	[InlineData("")]
	[InlineData("dns.google")]
	[InlineData("10.1")]
	[InlineData("1.1.1.1:0")]
	[InlineData("1.1.1.1:70000")]
	[InlineData("[2001:db8::53")]
	[InlineData("[2001:db8::53]x")]
	[InlineData("0.0.0.0")]
	[InlineData("255.255.255.255")]
	[InlineData("224.0.0.251")]
	public void ServerAddress_RefusesWhatCannotAnswer(string text)
	{
		Assert.False(DnsServerAddress.TryParse(text, out _, out string? problem));
		Assert.NotNull(DnsServerAddress.Problem(text));
		Assert.False(string.IsNullOrEmpty(problem));
	}

	[Theory]
	[InlineData(DnsResolverKind.Cloudflare, "1.1.1.1")]
	[InlineData(DnsResolverKind.Google, "8.8.8.8")]
	[InlineData(DnsResolverKind.Quad9, "9.9.9.9")]
	public void Resolvers_PutThePrimaryAddressFirst(DnsResolverKind kind, string first)
	{
		IReadOnlyList<IPEndPoint> servers = DnsResolvers.Servers(kind, null, out string? problem);

		Assert.Null(problem);
		Assert.Equal(2, servers.Count);
		Assert.Equal(new IPEndPoint(IPAddress.Parse(first), 53), servers[0]);
	}

	[Fact]
	public void Resolvers_SayWhyACustomOneCannotBeUsed()
	{
		Assert.Empty(DnsResolvers.Servers(DnsResolverKind.Custom, "", out string? problem));
		Assert.NotNull(problem);
		Assert.Single(DnsResolvers.Servers(DnsResolverKind.Custom, "10.0.0.53:5353", out _));
	}

	[Fact]
	public void SystemServers_LeaveOutTheSiteLocalPlaceholders() =>
		Assert.DoesNotContain(DnsResolvers.SystemServers(), address => address.IsIPv6SiteLocal);
}
