using System.Globalization;

namespace Moka.Network.Server;

/// <summary>What a TCP check from the server came back as, in words.</summary>
public enum ReachState
{
	Open,

	/// <summary>The server reached the port and was refused, which most services do when nothing listens there.</summary>
	Closed,

	/// <summary>No answer at all within the wait the check asked for.</summary>
	NoAnswer,

	/// <summary>The server's own network said the host cannot be reached.</summary>
	Unreachable,

	/// <summary>Neither open nor closed in a way the tool can name; the detail says what the server said.</summary>
	Failed,

	/// <summary>Nothing ran: no session, no feature, or the tool gave up waiting.</summary>
	DidNotRun,
}

/// <summary>A TCP check's answer: the state in one word and what the server actually said.</summary>
public sealed record ReachVerdict(ReachState State, string Detail)
{
	public string Word => State switch
	{
		ReachState.Open => "open",
		ReachState.Closed => "closed",
		ReachState.NoAnswer => "no answer",
		ReachState.Unreachable => "unreachable",
		ReachState.Failed => "failed",
		_ => "did not run",
	};
}

/// <summary>
/// The commands the Reach tab runs on the server, each built only from words this plugin checked itself and quoted
/// with <see cref="ServerQuote"/>, and the reading of what came back. The host a check names has to come out of
/// <see cref="Moka.Network.Targets.HostInput.TryParse"/>, which is why none of it can carry shell characters.
/// </summary>
public static class ServerReach
{
	public const int MinTcpSeconds = 1;

	public const int MaxTcpSeconds = 10;

	public const int MinPingCount = 1;

	public const int MaxPingCount = 20;

	/// <summary>The time a reply to ping waits for, in seconds, which <c>-W</c> takes on iputils and busybox ping alike.</summary>
	public const int PingReplyWaitSeconds = 2;

	/// <summary>Hops a trace stops at; enough to cross most networks without waiting through a long one.</summary>
	public const int MaxHops = 20;

	/// <summary>The exit status GNU coreutils <c>timeout</c> reports when it had to kill what it was watching.</summary>
	public const int TimeoutExitCode = 124;

	/// <summary>
	/// <c>timeout N bash -c '&lt;/dev/tcp/host/port'</c>: bash's built-in redirection, which needs no tools beyond the
	/// shell itself, ends at once when the port answers, and says why when it does not.
	/// </summary>
	public static string TcpCommand(string host, int port, int seconds) => ServerQuote.Line(
	[
		"timeout",
		seconds.ToString(CultureInfo.InvariantCulture),
		"bash",
		"-c",
		$"</dev/tcp/{host}/{port.ToString(CultureInfo.InvariantCulture)}",
	]);

	/// <summary><c>ping -n -c N -W W host</c>: numbers only, N requests, W seconds of wait per reply.</summary>
	public static string PingCommand(string host, int count, int waitSeconds = PingReplyWaitSeconds) => ServerQuote.Line(
	[
		"ping",
		"-n",
		"-c",
		count.ToString(CultureInfo.InvariantCulture),
		"-W",
		waitSeconds.ToString(CultureInfo.InvariantCulture),
		host,
	]);

	/// <summary><c>tracepath -n host</c>: one probe per hop, no name lookups, the same hop cap as traceroute here.</summary>
	public static string TracePathCommand(string host) => ServerQuote.Line(["tracepath", "-n", "-m", MaxHops.ToString(CultureInfo.InvariantCulture), host]);

	/// <summary><c>traceroute -n -w 2 -q 1 -m 20 host</c>: one probe per hop, two seconds per answer, numbers only.</summary>
	public static string TracerouteCommand(string host) => ServerQuote.Line(
	[
		"traceroute",
		"-n",
		"-w",
		"2",
		"-q",
		"1",
		"-m",
		MaxHops.ToString(CultureInfo.InvariantCulture),
		host,
	]);

	/// <summary>How long the tool waits around a ping of <paramref name="count"/> requests before it gives up.</summary>
	public static TimeSpan PingRunTimeout(int count, int waitSeconds) => TimeSpan.FromSeconds(count * (waitSeconds + 1) + 5);

	/// <summary>How long the tool waits around a trace before it gives up: every hop's wait, twice over for the probes.</summary>
	public static TimeSpan TraceRunTimeout => TimeSpan.FromSeconds(MaxHops * 4 + 10);

	/// <summary>Reads a TCP check's end into a verdict, from the exit status when bash said nothing and from its words when it spoke.</summary>
	public static ReachVerdict TcpVerdict(ServerRun run)
	{
		ArgumentNullException.ThrowIfNull(run);
		if (run.NeverRan || run.TimedOut || run.Broke)
		{
			return new ReachVerdict(ReachState.DidNotRun, run.Problem ?? "");
		}

		if (run.ExitCode == 0)
		{
			return new ReachVerdict(ReachState.Open, "The server connected and the port answered.");
		}

		if (run.ExitCode == TimeoutExitCode)
		{
			return new ReachVerdict(ReachState.NoAnswer, "The port did not answer before the check gave up.");
		}

		string said = FirstLine(run.Error.Length > 0 ? run.Error : run.Output);
		return Said(said) ?? new ReachVerdict(ReachState.Failed, said.Length > 0 ? said : $"The check ended with status {run.ExitCode}.");
	}

	/// <summary>What bash said, when its words name a state the tool knows: refused, no route, unreachable, a silent timeout.</summary>
	internal static ReachVerdict? Said(string text)
	{
		if (text.Length == 0)
		{
			return null;
		}

		if (text.Contains("refused", StringComparison.OrdinalIgnoreCase))
		{
			return new ReachVerdict(ReachState.Closed, text);
		}

		if (text.Contains("no route", StringComparison.OrdinalIgnoreCase) || text.Contains("unreachable", StringComparison.OrdinalIgnoreCase))
		{
			return new ReachVerdict(ReachState.Unreachable, text);
		}

		if (text.Contains("timed out", StringComparison.OrdinalIgnoreCase) || text.Contains("timeout", StringComparison.OrdinalIgnoreCase))
		{
			return new ReachVerdict(ReachState.NoAnswer, text);
		}

		return null;
	}

	private static string FirstLine(string text)
	{
		int end = text.IndexOf('\n');
		string line = (end < 0 ? text : text[..end]).Trim();
		return line.StartsWith("bash: ", StringComparison.Ordinal) ? line["bash: ".Length..].Trim() : line;
	}
}
