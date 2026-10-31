using System.Net;
using Moka.Network.Server;

namespace Moka.Network.Tests.Server;

public sealed class GetentHostsTests
{
	[Fact]
	public void Parse_ReadsTheAnswerOfAForwardLookup()
	{
		string text = """
			93.184.216.34    example.com
			2606:2800:21f:cb07:6820:80da:af6b:8b2c     example.com
			""";

		IReadOnlyList<GetentHost> hosts = GetentHosts.Parse(text);

		Assert.Equal(2, hosts.Count);
		Assert.Equal(IPAddress.Parse("93.184.216.34"), hosts[0].Address);
		Assert.Equal("example.com", hosts[0].Name);
		Assert.False(hosts[0].IsIPv6);
		Assert.Equal(IPAddress.Parse("2606:2800:21f:cb07:6820:80da:af6b:8b2c"), hosts[1].Address);
		Assert.True(hosts[1].IsIPv6);
	}

	[Fact]
	public void Parse_ReadsTheReverseAnswerWithItsAliases()
	{
		string text = "10.0.0.5      server.lan.example server example\n";

		IReadOnlyList<GetentHost> hosts = GetentHosts.Parse(text);

		GetentHost host = Assert.Single(hosts);
		Assert.Equal("server.lan.example", host.Name);
		Assert.Equal(["server", "example"], host.Aliases);
	}

	[Fact]
	public void Parse_SkipsWhatItDoesNotUnderstandRatherThanGuessing()
	{
		string text = """
			# a hosts file comment that slipped through
			not-an-address example.com
			93.184.216.34

			93.184.216.35 example.com
			""";

		IReadOnlyList<GetentHost> hosts = GetentHosts.Parse(text);

		GetentHost host = Assert.Single(hosts);
		Assert.Equal(IPAddress.Parse("93.184.216.35"), host.Address);
	}

	[Fact]
	public void Parse_EmptyIsAnEmptyAnswer()
	{
		Assert.Empty(GetentHosts.Parse(""));
	}
}
