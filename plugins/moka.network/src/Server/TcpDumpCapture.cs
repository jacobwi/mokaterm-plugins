namespace Moka.Network.Server;

/// <summary>
/// How the capture tab runs tcpdump and reads its end. The command is always <c>tcpdump -nn -l EXPRESSION</c>: numbers
/// only, line buffered, and the expression built by <see cref="TcpDumpFilter"/> out of three checked fields, passed as
/// one quoted word so nothing typed can ever become part of the shell line. The tool cannot raise its own rights, so
/// a permission failure is said plainly as the account's to solve.
/// </summary>
public static class TcpDumpCapture
{
	/// <summary>How often the capture tab redraws while packets arrive: often enough to feel live, rare enough to stay cheap.</summary>
	public static readonly TimeSpan RedrawEvery = TimeSpan.FromMilliseconds(250);

	public static string Command(TcpDumpFilter filter)
	{
		ArgumentNullException.ThrowIfNull(filter);
		return ServerQuote.Line(["tcpdump", "-nn", "-l", filter.Expression]);
	}

	/// <summary>
	/// Why a capture that ended by itself cannot go on, in plain words, or null when it ended cleanly. A permission
	/// failure gets the one wording that helps: tcpdump needs root on most servers, and only the account can get that.
	/// </summary>
	public static string? Refusal(ServerRun run)
	{
		ArgumentNullException.ThrowIfNull(run);
		if (run.NeverRan || run.TimedOut || run.Broke)
		{
			return run.Problem;
		}

		if (run.ExitCode == 0)
		{
			return null;
		}

		string said = run.Error.Length > 0 ? run.Error : run.Output;
		if (said.Contains("permission", StringComparison.OrdinalIgnoreCase)
			|| said.Contains("not permitted", StringComparison.OrdinalIgnoreCase)
			|| said.Contains("privileged", StringComparison.OrdinalIgnoreCase))
		{
			return "The account this session logs in as may not capture packets. tcpdump needs root on most servers, so the account itself has to have that right — log in as root, or ask the server's admin for sudo or a capture group. This tool cannot raise its own rights.";
		}

		return said.Length > 0 ? FirstLine(said) : $"tcpdump ended with status {run.ExitCode}.";
	}

	private static string FirstLine(string text)
	{
		int end = text.IndexOf('\n');
		string line = (end < 0 ? text : text[..end]).Trim();
		return line.StartsWith("tcpdump: ", StringComparison.Ordinal) ? line["tcpdump: ".Length..].Trim() : line;
	}
}
