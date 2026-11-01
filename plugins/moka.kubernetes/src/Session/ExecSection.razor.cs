using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Moka.Kubernetes.Server;
using Moka.Kubernetes.Settings;
using Mokaterm.Abstractions.Commands;

namespace Moka.Kubernetes.Session;

/// <summary>
/// The command runner: one command typed here, sent with <c>kubectl exec -- sh -c</c> into a pod's container, run to
/// its end, its stdout and stderr captured and shown. That is the whole honesty of this tab: the session's command
/// feature is one-shot, so there is no terminal on the other side, and a command that waits for input cannot work.
/// The pod's own JSON record fills the container picker, and the command is checked before it is ever sent.
/// </summary>
public sealed partial class ExecSection : SessionSectionBase
{
	private string _pod = "";
	private string? _container;
	private IReadOnlyList<string> _containers = [];
	private string _podKnown = "";
	private string? _space;
	private string _command = "";
	private string? _error;
	private ServerRun? _ran;

	private bool Offline => Session is not null && !Connected;

	private string Summary => Busy
		? "Running the command"
		: _ran is { } ran
			? ran.ExitCode == 0 ? "the command answered" : $"ended with status {ran.ExitCode}"
			: "nothing run yet";

	private string? PrimaryTitle => !Connected ? "The session is not connected." : null;

	/// <summary>
	/// The pods tab hands a pod over: its name and namespace land in the fields, the container picker fills from the
	/// pod's record, and the command line waits.
	/// </summary>
	public void Show(PodSelection selection)
	{
		_pod = selection.Pod;
		_space = selection.Namespace;
		_container = selection.Container;
		_containers = [];
		_podKnown = "";
		_ran = null;
		_error = null;
		_ = ReadContainersAsync();
	}

	private async Task PrimaryAsync()
	{
		if (Running)
		{
			Stop();
			return;
		}

		await RunAsync();
	}

	private async Task OnKeyAsync(KeyboardEventArgs args)
	{
		if (args.Key == "Enter" && !Busy)
		{
			await RunAsync();
		}
	}

	/// <summary>The picker's value: the chosen container's name, or "" for the first one.</summary>
	private string Container => _container ?? "";

	private Task OnContainerChanged(string value)
	{
		_container = value.Length > 0 ? value : null;
		return Task.CompletedTask;
	}

	private static string ContainerLabel(string container) => container.Length > 0 ? container : "First container";

	private async Task RunAsync()
	{
		_pod = _pod.Trim();
		_command = _command.Trim();
		if (_pod.Length == 0)
		{
			_error = "Name a pod first: the pods tab can hand one over with its Run a command action.";
			return;
		}

		if (RemoteCommandRequest.Problem(_command) is { } notSendable)
		{
			_error = notSendable;
			return;
		}

		if (Runner is not { } runner)
		{
			return;
		}

		_error = null;
		CancellationTokenSource run = BeginRun();
		StateHasChanged();
		try
		{
			KubernetesSettings settings = Settings;
			await ReadContainersAsync(run.Token);

			ServerRun ended = await runner.RunAsync(
				Kubectl.ExecCommand(settings, _pod, _container, _space, _command),
				settings.CommandTimeout,
				ServerRunner.RunOutputBytes,
				cancellationToken: run.Token);
			Post(() => _ran = ended);
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			// Stopped: what the command wrote so far was not kept, and the tab says so by staying empty.
		}
		finally
		{
			EndRun(run);
		}
	}

	/// <summary>
	/// Fills the container picker from the pod's record once per pod, quietly: a picker that cannot fill still runs.
	/// The choice is applied here rather than through <see cref="Post"/>, because the exec ask that follows reads it.
	/// </summary>
	private async Task ReadContainersAsync(CancellationToken cancellationToken = default)
	{
		if (_podKnown == _pod || _pod.Length == 0 || Runner is not { } runner)
		{
			return;
		}

		KubernetesSettings settings = Settings;
		ServerRun read = await runner.RunAsync(
			Kubectl.PodCommand(settings, _pod, _space),
			settings.CommandTimeout,
			ServerRunner.ListingOutputBytes,
			cancellationToken: cancellationToken);
		if (read.Ok && PodDetail.Parse(read.Output) is { } detail)
		{
			_containers = detail.ContainerNames;
			_podKnown = _pod;
			if (_container is not { Length: > 0 } || !_containers.Contains(_container))
			{
				_container = _containers.Count > 0 ? _containers[0] : null;
			}
		}
	}

	/// <summary>stdout and stderr of a run, one after the other, which is the honest order: the command wrote both.</summary>
	private static string Combined(ServerRun run) => run.Error.Length == 0 ? run.Output : $"{run.Output}\n{run.Error}".Trim('\n');
}
