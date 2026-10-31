using System.Net;
using Moka.Network.Server;

namespace Moka.Network.Tests.Server;

public sealed class ResolvConfTests
{
	[Fact]
	public void Nameservers_ReadsTheResolverLinesInTheirOrder()
	{
		string text = """
			# Managed by NetworkManager
			search lan.example example
			nameserver 10.0.0.53
			nameserver 2001:db8::53
			options ndots:2
			""";

		IReadOnlyList<IPAddress> servers = ResolvConf.Nameservers(text);

		Assert.Equal([IPAddress.Parse("10.0.0.53"), IPAddress.Parse("2001:db8::53")], servers);
	}

	[Fact]
	public void Nameservers_SkipsANameThatDoesNotReadAsAnAddress()
	{
		string text = """
			nameserver 10.0.0.53
			nameserver not-an-address
			""";

		IReadOnlyList<IPAddress> servers = ResolvConf.Nameservers(text);

		Assert.Equal([IPAddress.Parse("10.0.0.53")], servers);
	}

	[Fact]
	public void Nameservers_LeavesCommentsAndSemicolonLinesAlone()
	{
		string text = """
			; another resolver's comment style
			# a comment
			domain example
			""";

		Assert.Empty(ResolvConf.Nameservers(text));
	}

	[Fact]
	public void Nameservers_SplitsOnTabsTheWayTheFileIsWritten()
	{
		string text = "nameserver\t10.0.0.53\n";

		IReadOnlyList<IPAddress> servers = ResolvConf.Nameservers(text);

		Assert.Equal([IPAddress.Parse("10.0.0.53")], servers);
	}

	[Fact]
	public void Nameservers_LeavesALineWithMoreAfterTheAddressAlone()
	{
		string text = "nameserver 10.0.0.53 # an inline note\n";

		Assert.Empty(ResolvConf.Nameservers(text));
	}
}
