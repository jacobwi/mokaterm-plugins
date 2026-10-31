using System.Text;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Sessions;

namespace Moka.Network.Server;

/// <summary>
/// Runs a command on the session's server through <see cref="IRemoteCommandFeature"/>, for every tab of the session
/// tool: it quotes the words with <see cref="ServerQuote"/>, sets <c>LC_ALL=C</c> so every tool on the server answers
/// in one locale, gives up after a timeout the caller picks per action, caps what is kept, and turns every way a run
/// cannot happen into a plain <see cref="ServerRun"/> rather than an error. The feature is asked for again on every
/// run, so a session that reconnects between two runs is simply read again.
/// </summary>
public sealed class ServerRunner
{
	/// <summary>How much of each stream a quick command keeps, which is plenty for anything but a listing.</summary>
	public const int QuickOutputBytes = 64 * 1024;

	/// <summary>How much a listing such as <c>ss -tulpn</c> keeps; a host with a thousand listeners prints far less.</summary>
	public const int ListingOutputBytes = 1024 * 1024;

	/// <summary>What the probe of installed programs keeps; it answers one short line per program or nothing.</summary>
	public const int ProbeOutputBytes = RemoteCommandRequest.MinOutputLimitBytes;

	/// <summary>What the probe of installed programs may take before it counts as hung.</summary>
	public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

	/// <summary>How long a live capture keeps of each stream, which only feeds the error message: the ring holds the lines.</summary>
	public const int CaptureOutputBytes = 16 * 1024;

	private readonly Func<IRemoteCommandFeature?> _feature;

	/// <param name="feature">Asked again on every run, so it should read the session live rather than hold a feature.</param>
	public ServerRunner(Func<IRemoteCommandFeature?> feature) => _feature = feature;

	/// <summary>A runner for a session handle: the feature comes from the live session, which is null unless connected.</summary>
	public static ServerRunner For(ISessionHandle session)
	{
		ArgumentNullException.ThrowIfNull(session);
		return new ServerRunner(() => session.Session?.GetFeature<IRemoteCommandFeature>());
	}

	/// <summary>True when there is a connected session with a command feature to run on right now.</summary>
	public bool Ready => _feature() is not null;

	/// <summary>
	/// Runs <paramref name="words"/>, already quoted with <see cref="ServerQuote"/> or built from validated values
	/// only, under <c>LC_ALL=C</c>. Cancelling the token stops the run for the caller to see as its own stop; only the
	/// tool's own timeout turns into a <see cref="ServerRun.TimedOut"/>.
	/// </summary>
	public async Task<ServerRun> RunAsync(
		string command,
		TimeSpan timeout,
		int outputLimitBytes = QuickOutputBytes,
		bool runsLong = false,
		CommandOutputHandler? onOutput = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);
		IRemoteCommandFeature? feature = _feature();
		if (feature is null)
		{
			return ServerRun.Absent("The session is not connected, so nothing can run on the server.");
		}

		string line = "LC_ALL=C " + command;
		RemoteCommandRequest request = new()
		{
			Command = line,
			OutputLimitBytes = Math.Max(outputLimitBytes, RemoteCommandRequest.MinOutputLimitBytes),
			RunsLong = runsLong,
			OnOutput = onOutput,
		};

		try
		{
			using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			limit.CancelAfter(timeout);
			RemoteCommandOutcome outcome = await feature.RunAsync(request, limit.Token).ConfigureAwait(false);
			return ServerRun.FromOutcome(line, outcome);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (OperationCanceledException)
		{
			return ServerRun.GaveUp(line, timeout);
		}
		catch (RemoteCommandException ex)
		{
			// The host words these for the user already: it could not start, the link dropped, or a stream overran
			// what the protocol reads for one command.
			return ServerRun.Failed(line, ex.Message);
		}
	}

	/// <summary>
	/// Which of <paramref name="programs"/> the server has, in one round trip: a loop over <c>command -v</c> that
	/// echoes the name of each one it finds. The program names are this plugin's own constants, never user text.
	/// </summary>
	public async Task<IReadOnlySet<string>> ProgramsAsync(
		IReadOnlyList<string> programs,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(programs);
		if (programs.Count == 0)
		{
			return new HashSet<string>(StringComparer.Ordinal);
		}

		foreach (string program in programs)
		{
			// Defense in depth: the loop's own words must never need quoting.
			if (program.Length == 0 || !program.All(char.IsAsciiLetterOrDigit))
			{
				throw new ArgumentException($"Program names must be plain words: {program}.", nameof(programs));
			}
		}

		StringBuilder script = new("for p in ");
		script.Append(string.Join(' ', programs));
		script.Append("; do command -v \"$p\" >/dev/null 2>&1 && echo \"$p\"; done");
		ServerRun run = await RunAsync("sh -c " + ServerQuote.Quote(script.ToString()), timeout ?? ProbeTimeout, ProbeOutputBytes,
			cancellationToken: cancellationToken).ConfigureAwait(false);
		if (!run.Ok)
		{
			return new HashSet<string>(StringComparer.Ordinal);
		}

		HashSet<string> present = new(StringComparer.Ordinal);
		foreach (string line in run.Output.Split('\n'))
		{
			string name = line.Trim();
			if (name.Length > 0)
			{
				present.Add(name);
			}
		}

		return present;
	}
}
