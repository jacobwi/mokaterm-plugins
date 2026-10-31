using Moka.Network.Server;

namespace Moka.Network.Tests.Server;

public sealed class ServerReachTests
{
	[Fact]
	public void TcpCommand_WrapsEveryWordIncludingTheBashScript()
	{
		Assert.Equal("'timeout' '5' 'bash' '-c' '</dev/tcp/example.com/443'", ServerReach.TcpCommand("example.com", 443, 5));
	}

	[Fact]
	public void TcpCommand_QuotesAHostWithAnythingShellShapedInIt()
	{
		// A host like this never gets past the field's own checks; if it somehow did, it still could not run anything.
		Assert.Equal("'timeout' '3' 'bash' '-c' '</dev/tcp/evil host;rm/80'", ServerReach.TcpCommand("evil host;rm", 80, 3));
	}

	[Fact]
	public void PingCommand_WritesNumbersAndTheHostAsQuotedWords()
	{
		Assert.Equal("'ping' '-n' '-c' '4' '-W' '2' 'example.com'", ServerReach.PingCommand("example.com", 4));
	}

	[Fact]
	public void TracePathCommand_WalksTheSameHopCapAsTraceroute()
	{
		Assert.Equal("'tracepath' '-n' '-m' '20' 'example.com'", ServerReach.TracePathCommand("example.com"));
		Assert.Equal("'traceroute' '-n' '-w' '2' '-q' '1' '-m' '20' 'example.com'", ServerReach.TracerouteCommand("example.com"));
	}

	[Fact]
	public void PingRunTimeout_CoversEveryRequestWithItsOwnWaitAndABreath()
	{
		Assert.Equal(TimeSpan.FromSeconds(4 * 3 + 5), ServerReach.PingRunTimeout(4, 2));
	}

	[Theory]
	[InlineData(0, "", ReachState.Open)]
	[InlineData(124, "", ReachState.NoAnswer)]
	[InlineData(1, "bash: connect: Connection refused", ReachState.Closed)]
	[InlineData(1, "bash: line 0: /dev/tcp/10.0.0.1/80: No route to host", ReachState.Unreachable)]
	[InlineData(1, "bash: line 0: /dev/tcp/2001:db8::1/80: Connection timed out", ReachState.NoAnswer)]
	[InlineData(1, "bash: /dev/tcp/example.com/80: Permission denied", ReachState.Failed)]
	[InlineData(2, "", ReachState.Failed)]
	public void TcpVerdict_ReadsTheExitStatusWhenBashSaidNothing(int exitCode, string error, ReachState state)
	{
		ServerRun run = RunWith(exitCode, error);

		ReachVerdict verdict = ServerReach.TcpVerdict(run);

		Assert.Equal(state, verdict.State);
		Assert.NotEqual("", verdict.Word);
	}

	[Fact]
	public void TcpVerdict_WhenNothingRan_SaysThatAndNotSomethingAboutThePort()
	{
		ServerRun run = ServerRun.GaveUp("timeout '5' bash", TimeSpan.FromSeconds(5));

		ReachVerdict verdict = ServerReach.TcpVerdict(run);

		Assert.Equal(ReachState.DidNotRun, verdict.State);
		Assert.Equal("did not run", verdict.Word);
	}

	[Fact]
	public void TcpVerdict_TheDetailCarriesWhatTheServerSaidWithoutTheShellPrefix()
	{
		ReachVerdict verdict = ServerReach.TcpVerdict(RunWith(1, "bash: connect: Connection refused\nbash: more lines"));

		Assert.Equal("connect: Connection refused", verdict.Detail);
	}

	private static ServerRun RunWith(int exitCode, string error) => new()
	{
		Command = "timeout '5' bash -c '</dev/tcp/h/80'",
		ExitCode = exitCode,
		Error = error,
		Output = "",
		Duration = TimeSpan.FromSeconds(0.1),
	};
}
