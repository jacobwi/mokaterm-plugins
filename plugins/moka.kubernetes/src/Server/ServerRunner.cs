using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.Sessions;

namespace Moka.Kubernetes.Server;

/// <summary>
/// Runs one kubectl command at a time on the session's server through <see cref="IRemoteCommandFeature"/>: it quotes
/// the words with <see cref="ServerQuote"/>, sets <c>LC_ALL=C</c> so kubectl answers in one locale, gives up after a
/// timeout the caller picks per action, caps what is kept, and turns every way a run cannot happen into a plain
/// <see cref="ServerRun"/> rather than an error. The feature is asked for again on every run, so a session that
/// reconnects between two runs is simply read again. Nothing here holds a channel open: every ask is one command to
/// its end, which is what an exec channel gives and all this plugin ever needs.
/// </summary>
public sealed class ServerRunner
{
	/// <summary>How much of each stream a quick command keeps: versions, contexts, one pod's JSON.</summary>
	public const int QuickOutputBytes = 64 * 1024;

	/// <summary>How much a listing such as <c>kubectl get pods -o wide</c> keeps, and one pod's full JSON.</summary>
	public const int ListingOutputBytes = 1024 * 1024;

	/// <summary>How much the logs and the command runner keep of each stream: a long tail or a chatty command.</summary>
	public const int RunOutputBytes = 256 * 1024;

	/// <summary>What the probe of kubectl itself keeps; it answers one short line or nothing.</summary>
	public const int ProbeOutputBytes = RemoteCommandRequest.MinOutputLimitBytes;

	/// <summary>What the probe of kubectl may take before it counts as hung.</summary>
	public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);

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
	/// Runs <paramref name="command"/>, already quoted with <see cref="ServerQuote"/> or built from validated values
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
	/// Whether the server has the kubectl this plugin is set to call, in one round trip: <c>command -v</c> on the
	/// configured path, which answers its location or nothing. The path is this plugin's own validated setting, never
	/// free user text on the line unquoted.
	/// </summary>
	public async Task<bool> HasKubectlAsync(string kubectlPath, CancellationToken cancellationToken = default)
	{
		string program = string.IsNullOrWhiteSpace(kubectlPath) ? "kubectl" : kubectlPath.Trim();
		ServerRun run = await RunAsync("command -v " + ServerQuote.Quote(program), ProbeTimeout, ProbeOutputBytes,
			cancellationToken: cancellationToken).ConfigureAwait(false);
		return run.Ok && run.Output.Trim().Length > 0;
	}
}
