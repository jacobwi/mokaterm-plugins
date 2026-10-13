using Moka.Certificates.Tls;

namespace Moka.Certificates.Tests.Tls;

public sealed class TlsTargetTests
{
	[Theory]
	[InlineData("example.com", "", "", "example.com", 443, "example.com")]
	[InlineData("Example.COM", "8443", "", "example.com", 8443, "example.com")]
	[InlineData("example.com:9443", "", "", "example.com", 9443, "example.com")]
	[InlineData("https://www.example.com:8443/path?q=1", "", "", "www.example.com", 8443, "www.example.com")]
	[InlineData("https://www.example.com/", "", "", "www.example.com", 443, "www.example.com")]
	[InlineData("192.0.2.7", "", "", "192.0.2.7", 443, null)]
	[InlineData("192.0.2.7", "", "internal.example", "192.0.2.7", 443, "internal.example")]
	[InlineData("[2001:db8::7]:4443", "", "", "2001:db8::7", 4443, null)]
	[InlineData("2001:db8::7", "443", "", "2001:db8::7", 443, null)]
	[InlineData("bücher.example", "", "", "xn--bcher-kva.example", 443, "xn--bcher-kva.example")]
	public void TryParse_ReadsWhatPeopleType(string host, string port, string sni, string expectedHost, int expectedPort, string? expectedSni)
	{
		Assert.True(TlsTarget.TryParse(host, port, sni, out TlsTarget? target, out string? problem), problem);

		Assert.Equal(expectedHost, target.Host);
		Assert.Equal(expectedPort, target.Port);
		Assert.Equal(expectedSni, target.ServerName);
	}

	[Theory]
	[InlineData("", "", "", "Type a host name")]
	[InlineData("bad host", "", "", "not a host name")]
	[InlineData("example.com", "0", "", "1 to 65535")]
	[InlineData("example.com", "70000", "", "1 to 65535")]
	[InlineData("example.com", "443", "192.0.2.1", "SNI carries a name")]
	public void TryParse_SaysWhatIsWrong(string host, string port, string sni, string expected)
	{
		Assert.False(TlsTarget.TryParse(host, port, sni, out _, out string? problem));

		Assert.Contains(expected, problem, StringComparison.Ordinal);
	}

	[Fact]
	public void Endpoint_BracketsIpv6() => Assert.Equal("[2001:db8::7]:443", new TlsTarget("2001:db8::7", 443, null).Endpoint);
}
