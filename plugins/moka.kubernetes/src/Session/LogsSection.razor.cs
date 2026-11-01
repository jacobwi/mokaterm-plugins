using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Moka.Kubernetes.Server;
using Moka.Kubernetes.Settings;
using Mokaterm.UI.Common.ToolPages;

namespace Moka.Kubernetes.Session;

/// <summary>
/// The log of one pod's container: the tail, <c>--tail=500 --timestamps</c>, in a table newest at the bottom, and a
/// Follow toggle that asks kubectl every 3 seconds for what the newest line's timestamp has brought — a poll, never a
/// stream, because every run through the session's command feature is one command to its end. The container list
/// comes from the pod's own JSON record, so the picker offers what the pod really runs. Previous reads the container's
/// log from before its latest start, which is what a restarter wants.
/// </summary>
public sealed partial class LogsSection : SessionSectionBase
{
	/// <summary>How often Follow asks for new lines.</summary>
	private static readonly TimeSpan FollowEvery = TimeSpan.FromSeconds(3);

	private string _pod = "";
	private string? _container;
	private IReadOnlyList<string> _containers = [];
	private string _podKnown = "";
	private string? _space;
	private bool _previous;
	private bool _following;
	private bool _read;
	private string? _error;
	private LogLines _lines = new();
	private IReadOnlyList<PodLogLine> _rows = [];
	private PagePoller? _poller;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	private bool Offline => Session is not null && !Connected;

	private string Summary => _lines.Count > 0
		? $"{Count(_lines.Count)} lines of {_pod}{(_container is { Length: > 0 } container ? $" ({container})" : "")}{(_previous ? ", previous" : "")}"
		: Busy ? "Reading the log" : "no log read yet";

	private string? PrimaryTitle => !Connected ? "The session is not connected." : null;

	/// <summary>
	/// The pods tab hands a pod over: its name and namespace land in the fields and the log is asked for at once. The
	/// container picker fills itself from the pod's record, then the first container is the choice.
	/// </summary>
	public void Show(PodSelection selection)
	{
		_pod = selection.Pod;
		_space = selection.Namespace;
		_container = selection.Container;
		_containers = [];
		_podKnown = "";
		_ = FetchAsync();
	}

	protected override void OnParametersSet()
	{
		// A hidden tab keeps what it read, but Follow's clock is only worth running where somebody sees it.
		if (!IsActive && _poller is { IsRunning: true })
		{
			_following = false;
			StopFollow();
		}
	}

	private async Task PrimaryAsync()
	{
		if (Running)
		{
			Stop();
			return;
		}

		await FetchAsync();
	}

	private async Task OnKeyAsync(KeyboardEventArgs args)
	{
		if (args.Key == "Enter" && !Busy)
		{
			await FetchAsync();
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

	private Task OnPreviousChanged(bool value)
	{
		_previous = value;
		return Task.CompletedTask;
	}

	private async Task OnFollowChanged(bool value)
	{
		_following = value;
		if (_following)
		{
			await FetchAsync();
		}
		else
		{
			StopFollow();
		}
	}

	private async Task FetchAsync()
	{
		_pod = _pod.Trim();
		if (_pod.Length == 0)
		{
			_error = "Name a pod first: the pods tab can hand one over with its Logs action.";
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
			await ReadContainersAsync(runner, settings, run.Token);

			ServerRun logs = await runner.RunAsync(
				Kubectl.LogsCommand(settings, _pod, _container, _space, _previous),
				settings.CommandTimeout,
				ServerRunner.RunOutputBytes,
				cancellationToken: run.Token);
			Post(() =>
			{
				_read = true;
				if (logs.Ok)
				{
					_lines.Clear();
					_lines.Append(logs.Output);
					_rows = _lines.Snapshot();
				}
				else
				{
					_rows = [];
					_error = KubectlErrors.Words(logs);
				}
			});
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			// Stopped: the lines read so far stay.
		}
		finally
		{
			EndRun(run);
			if (_following && !Disposed)
			{
				StartFollow();
			}
		}
	}

	/// <summary>
	/// One Follow tick: ask for what the newest line's timestamp has brought and append it, stopping Follow and saying
	/// why when the ask itself fails, which is usually the pod or the connection going away.
	/// </summary>
	private async Task PollAsync()
	{
		if (Runner is not { } runner || Busy)
		{
			return;
		}

		KubernetesSettings settings = Settings;
		string? since = _lines.Newest() is { } newest ? PodLogs.SinceTime(newest) : null;
		ServerRun logs = await runner.RunAsync(
			Kubectl.LogsCommand(settings, _pod, _container, _space, previous: false, sinceTime: since),
			settings.CommandTimeout,
			ServerRunner.RunOutputBytes);
		if (logs.Ok)
		{
			_lines.Append(logs.Output);
			_rows = _lines.Snapshot();
		}
		else
		{
			_following = false;
			StopFollow();
			_error = $"Follow stopped: {KubectlErrors.Words(logs)}";
		}

		StateHasChanged();
	}

	/// <summary>
	/// Fills the container picker from the pod's record once per pod, leaving it empty when the record will not read.
	/// The choice is applied here rather than through <see cref="Post"/>, because the log ask that follows reads it.
	/// </summary>
	private async Task ReadContainersAsync(ServerRunner runner, KubernetesSettings settings, CancellationToken cancellationToken)
	{
		if (_podKnown == _pod || _pod.Length == 0)
		{
			return;
		}

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

	private void StartFollow()
	{
		_poller ??= new PagePoller(Time, work => InvokeAsync(work), () => IsActive && Connected && _following && !Busy && !Disposed);
		_poller.Start(FollowEvery, PollAsync, CancellationToken.None);
	}

	private void StopFollow() => _poller?.Stop();

	protected override void Dispose(bool disposing)
	{
		_poller?.Dispose();
		_poller = null;
		base.Dispose(disposing);
	}
}
