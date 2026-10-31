using System.Globalization;
using Moka.Network.Server;
using Moka.Network.Targets;

namespace Moka.Network.Tests.Server;

public sealed class TcpDumpCaptureTests
{
	[Fact]
	public void Command_IsNumbersOnlyLineBufferedWithTheFilterAsOneQuotedWord()
	{
		string command = TcpDumpCapture.Command(Filter("example.com", 443, CaptureProtocol.Tcp));

		Assert.Equal("'tcpdump' '-nn' '-l' 'tcp and host example.com and port 443'", command);
	}

	[Fact]
	public void Command_QuotesWhateverTheTypedHostTurnedOutToBe()
	{
		// The fields' own checks stop a host like this; if one slipped through, the expression stays one word anyway.
		TcpDumpFilter filter = new() { Host = new HostTarget("a'b c", null), Port = 53, Protocol = CaptureProtocol.Udp };

		string command = TcpDumpCapture.Command(filter);

		Assert.Equal("'tcpdump' '-nn' '-l' " + ServerQuote.Quote("udp and host a'b c and port 53"), command);
	}

	[Fact]
	public void Refusal_WhenTheAccountMayNotCapture_SaysWhoseProblemThatIs()
	{
		ServerRun run = RunWith(1, "tcpdump: socket: Operation not permitted\n");

		string? refusal = TcpDumpCapture.Refusal(run);

		Assert.NotNull(refusal);
		Assert.Contains("may not capture", refusal, StringComparison.Ordinal);
		Assert.Contains("needs root", refusal, StringComparison.Ordinal);
		Assert.Contains("cannot raise its own rights", refusal, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("tcpdump: You don't have permission to capture on that device")]
	[InlineData("tcpdump: eth0: You don't have permission to capture on this device")]
	[InlineData("tcpdump: bind: Permission denied")]
	public void Refusal_KnowsTheWaysAServerSaysNo(string error)
	{
		Assert.NotNull(TcpDumpCapture.Refusal(RunWith(1, error + "\n")));
	}

	[Fact]
	public void Refusal_ACleanEndIsNotARefusal()
	{
		Assert.Null(TcpDumpCapture.Refusal(RunWith(0, "tcpdump: listening on eth0, link-type EN10MB (Ethernet)\n0 packets captured\n")));
	}

	[Fact]
	public void Refusal_AnotherFailureCarriesTheServerOwnWordsWithoutTheProgramPrefix()
	{
		Assert.Equal("unknown wlan0", TcpDumpCapture.Refusal(RunWith(1, "tcpdump: unknown wlan0\n")));
	}

	[Fact]
	public void Refusal_ASilentFailureNamesTheStatus()
	{
		Assert.Equal("tcpdump ended with status 1.", TcpDumpCapture.Refusal(RunWith(1, "")));
	}

	[Fact]
	public void Refusal_WhenNothingCouldRun_CarriesTheRunnerOwnWords()
	{
		ServerRun run = ServerRun.Absent("The session is not connected, so nothing can run on the server.");

		Assert.Equal("The session is not connected, so nothing can run on the server.", TcpDumpCapture.Refusal(run));
	}

	private static TcpDumpFilter Filter(string host, int? port, CaptureProtocol protocol) =>
		TcpDumpFilter.TryRead(host, port?.ToString(CultureInfo.InvariantCulture), protocol, out _) ?? new TcpDumpFilter { Protocol = protocol };

	private static ServerRun RunWith(int exitCode, string error) => new()
	{
		Command = "tcpdump '-nn' '-l' 'tcp'",
		ExitCode = exitCode,
		Error = error,
		Output = "",
		Duration = TimeSpan.FromSeconds(0.2),
	};
}
