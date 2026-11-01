using System.Diagnostics.CodeAnalysis;
using Mokaterm.Abstractions.Commands;

namespace Moka.Kubernetes.Server;

/// <summary>
/// One command's end as the session tool reads it: either it ran and has an exit status and its output, or it never
/// ran, which every message here says in plain words rather than as an error. The command itself is kept because the
/// tool shows what ran, never because anything in it is secret: every word of it was built by this plugin or quoted
/// through <see cref="ServerQuote"/>.
/// </summary>
public sealed record ServerRun
{
	/// <summary>The command as it ran, <c>LC_ALL=C</c> included.</summary>
	public required string Command { get; init; }

	/// <summary>True when there was nothing to run on: no session, or no command feature on it.</summary>
	public bool NeverRan { get; init; }

	/// <summary>True when the tool gave up waiting, which is the tool's choice, not the server's.</summary>
	public bool TimedOut { get; init; }

	/// <summary>True when the command could not start or the connection dropped while it ran.</summary>
	public bool Broke { get; init; }

	/// <summary>Why it never ran, gave up or broke, in words for the user; null when it ran.</summary>
	public string? Problem { get; init; }

	public int? ExitCode { get; init; }

	public string? ExitSignal { get; init; }

	/// <summary>What the command wrote to stdout, kept up to the limit the run asked for.</summary>
	public string Output { get; init; } = "";

	/// <summary>What the command wrote to stderr, kept up to the same limit.</summary>
	public string Error { get; init; } = "";

	/// <summary>How long the server took, when it ran at all.</summary>
	public TimeSpan? Duration { get; init; }

	/// <summary>It ran to its end and returned 0, the only outcome a tab reads as success.</summary>
	[MemberNotNullWhen(false, nameof(Problem))]
	public bool Ok => !NeverRan && !TimedOut && !Broke && ExitCode == 0;

	/// <summary>stdout when it held anything, stderr otherwise: what a tab shows of a command that did not succeed.</summary>
	public string Text => Output.Length > 0 ? Output : Error;

	public static ServerRun FromOutcome(string command, RemoteCommandOutcome outcome) => new()
	{
		Command = command,
		ExitCode = outcome.ExitCode,
		ExitSignal = outcome.ExitSignal,
		Output = outcome.Output.ToText(),
		Error = outcome.Error.ToText(),
		Duration = outcome.Duration,
	};

	public static ServerRun Absent(string problem) => new() { Command = "", NeverRan = true, Problem = problem };

	public static ServerRun GaveUp(string command, TimeSpan after) => new()
	{
		Command = command,
		TimedOut = true,
		Problem = $"The server was still going after {after.TotalSeconds:0.#} s, so the tool stopped waiting.",
	};

	public static ServerRun Failed(string command, string problem) => new() { Command = command, Broke = true, Problem = problem };
}
