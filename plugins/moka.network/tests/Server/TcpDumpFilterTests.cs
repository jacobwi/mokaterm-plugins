using Moka.Network.Server;

namespace Moka.Network.Tests.Server;

public sealed class TcpDumpFilterTests
{
	[Fact]
	public void TryRead_WithNothingChosen_CapturesTheWholeProtocol()
	{
		TcpDumpFilter? filter = TcpDumpFilter.TryRead("", "", CaptureProtocol.Tcp, out string? problem);

		Assert.Null(problem);
		Assert.NotNull(filter);
		Assert.Equal("tcp", filter!.Expression);
	}

	[Theory]
	[InlineData("example.com", "", CaptureProtocol.Tcp, "tcp and host example.com")]
	[InlineData("10.0.0.5", "", CaptureProtocol.Udp, "udp and host 10.0.0.5")]
	[InlineData("2001:db8::1", "", CaptureProtocol.Icmp, "icmp and host 2001:db8::1")]
	[InlineData("", "443", CaptureProtocol.Tcp, "tcp and port 443")]
	[InlineData("example.com", "443", CaptureProtocol.Tcp, "tcp and host example.com and port 443")]
	public void TryRead_BuildsTheExpressionOutOfTheThreeFields(string host, string port, CaptureProtocol protocol, string expression)
	{
		TcpDumpFilter? filter = TcpDumpFilter.TryRead(host, port, protocol, out string? problem);

		Assert.Null(problem);
		Assert.Equal(expression, filter!.Expression);
	}

	[Theory]
	[InlineData("not a host!")]
	[InlineData("example.com:")]
	[InlineData("a b")]
	public void TryRead_RefusesAHostThatDoesNotReadAsOne(string host)
	{
		Assert.Null(TcpDumpFilter.TryRead(host, "", CaptureProtocol.Tcp, out string? problem));

		Assert.NotEqual("", problem);
	}

	[Theory]
	[InlineData("0", "from 1 to 65535")]
	[InlineData("65536", "from 1 to 65535")]
	[InlineData("-1", "from 1 to 65535")]
	[InlineData("https", "from 1 to 65535")]
	public void TryRead_SaysWhatIsWrongWithAPort(string port, string part)
	{
		Assert.Null(TcpDumpFilter.TryRead("", port, CaptureProtocol.Tcp, out string? problem));

		Assert.Contains(part, problem, StringComparison.Ordinal);
	}

	[Fact]
	public void TryRead_RefusesAPortOnIcmpBecauseIcmpHasNone()
	{
		Assert.Null(TcpDumpFilter.TryRead("", "443", CaptureProtocol.Icmp, out string? problem));

		Assert.Contains("icmp has no ports", problem, StringComparison.Ordinal);
	}

	[Fact]
	public void TryRead_TrimsWhatWasTyped()
	{
		TcpDumpFilter? filter = TcpDumpFilter.TryRead("  example.com  ", " 443 ", CaptureProtocol.Udp, out string? problem);

		Assert.Null(problem);
		Assert.Equal("udp and host example.com and port 443", filter!.Expression);
	}
}
