using Microsoft.AspNetCore.Components;
using Moka.Kubernetes.Server;
using Moka.Kubernetes.Settings;
using Moka.Red.Core.Enums;
using Mokaterm.UI.Common.PluginUi;

namespace Moka.Kubernetes.Session;

/// <summary>
/// The cluster's pods from <c>kubectl get pods -o wide</c>, one row each, and a details panel beside the list that
/// reads the picked pod's whole JSON record: its conditions, its containers with their images and states, its node
/// and IP. Which pods show is the namespace story: the setting's namespace, or the one kubectl defaults to, with the
/// All-namespaces switch for every one, and the tab says which of those it is reading. A pod in a state that needs
/// looking at carries a warning chip.
/// </summary>
public sealed partial class PodsSection : SessionSectionBase
{
	private IReadOnlyList<PodRow> _rows = [];
	private bool _listed;
	private bool _all;
	private string? _error;
	private PodRow? _selected;
	private PodDetail? _detail;
	private string? _detailError;
	private bool _detailBusy;

	/// <summary>The pods tab hands its picked pod to the logs tab.</summary>
	[Parameter]
	public EventCallback<PodSelection> OnOpenLogs { get; set; }

	/// <summary>The pods tab hands its picked pod to the command runner.</summary>
	[Parameter]
	public EventCallback<PodSelection> OnOpenCommand { get; set; }

	private bool Offline => Session is not null && !Connected;

	private int Unsure => _rows.Count(row => !row.Settled);

	private string Summary => !_listed
		? "Asking the cluster"
		: Unsure > 0
			? $"{Count(_rows.Count)} pods, {Count(Unsure)} to look at"
			: $"{Count(_rows.Count)} pods";

	/// <summary>Which pods this listing reads, said in one line, because the namespace story is easy to get wrong.</summary>
	private string ScopeNote => _all
		? "Every namespace, from kubectl's -A."
		: Settings.Namespace.Length > 0
			? $"Reading the {Settings.Namespace} namespace. Turn on All namespaces for every one."
			: "Reading only the namespace kubectl defaults to, which is the context's own; an empty listing here often means the pods sit in another one. Turn on All namespaces for every one.";

	private string Columns => _all
		? "--mk8s-columns: 0.9fr 1.6fr 64px 150px 70px 70px 1fr"
		: "--mk8s-columns: 1.6fr 64px 150px 70px 70px 1fr";

	private string? Subtitle => _detail is not { } detail
		? null
		: detail.Namespace.Length > 0 ? $"{detail.Namespace} · started {detail.StartTime}" : $"started {detail.StartTime}";

	private MokaColor DotColor => _selected is { } row ? PodTone(row.Status) : MokaColor.Surface;

	private string? PrimaryTitle => !Connected ? "The session is not connected." : null;

	/// <summary>The chip color for a status: green for running and finished well, blue for on its way, warning for the rest.</summary>
	internal static MokaColor PodTone(string status) => status switch
	{
		"Running" or "Succeeded" or "Completed" => MokaColor.Success,
		"ContainerCreating" or "PodInitializing" or "Terminating" => MokaColor.Info,
		_ => MokaColor.Warning,
	};

	protected override void OnSessionChange()
	{
		if (Connected && !_listed)
		{
			_ = ListAsync();
		}
	}

	protected override void OnAfterRender(bool firstRender)
	{
		if (firstRender && Connected && !_listed)
		{
			_ = ListAsync();
		}
	}

	private async Task PrimaryAsync()
	{
		if (Running)
		{
			Stop();
			return;
		}

		await ListAsync();
	}

	private async Task OnAllChanged(bool value)
	{
		_all = value;
		await ListAsync();
	}

	private async Task ListAsync()
	{
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
			ServerRun listing = await runner.RunAsync(
				Kubectl.PodsCommand(settings, _all),
				settings.CommandTimeout,
				ServerRunner.ListingOutputBytes,
				cancellationToken: run.Token);
			Post(() =>
			{
				_listed = true;
				if (listing.Ok)
				{
					_rows = PodListing.Parse(listing.Output, _all);
					_selected = StillThere();
				}
				else
				{
					_rows = [];
					_error = KubectlErrors.Words(listing);
				}
			});
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			// Stopped: the listing so far stays.
		}
		finally
		{
			EndRun(run);
		}
	}

	/// <summary>Reads the picked pod's record; a listing that dropped it unpicks it, which the caller checks.</summary>
	private PodRow? StillThere()
	{
		if (_selected is not { } picked)
		{
			return null;
		}

		PodRow? again = _rows.FirstOrDefault(row => row.Name == picked.Name && row.Namespace == picked.Namespace);
		if (again is null)
		{
			_detail = null;
			_detailError = null;
		}

		return again;
	}

	private async Task PickAsync(PodRow row)
	{
		if (Runner is not { } runner)
		{
			return;
		}

		_selected = row;
		_detail = null;
		_detailError = null;
		_detailBusy = true;
		StateHasChanged();
		CancellationTokenSource run = BeginRun();
		try
		{
			KubernetesSettings settings = Settings;
			ServerRun read = await runner.RunAsync(
				Kubectl.PodCommand(settings, row.Name, row.Namespace.Length > 0 ? row.Namespace : null),
				settings.CommandTimeout,
				ServerRunner.ListingOutputBytes,
				cancellationToken: run.Token);
			Post(() =>
			{
				_detailBusy = false;
				if (read.Ok && PodDetail.Parse(read.Output) is { } detail)
				{
					_detail = detail;
				}
				else if (read.Ok)
				{
					_detailError = "kubectl answered with something this tool could not read as a pod's record.";
				}
				else
				{
					_detailError = KubectlErrors.Words(read);
				}
			});
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			// Stopped or another pick replaced this one.
			Post(() => _detailBusy = false);
		}
		finally
		{
			EndRun(run);
		}
	}

	private void CloseDetail()
	{
		_selected = null;
		_detail = null;
		_detailError = null;
	}

	private Task LogsAsync() => _detail is { } detail && _selected is { } row
		? OnOpenLogs.InvokeAsync(new PodSelection(detail.Name, row.Namespace.Length > 0 ? row.Namespace : null, null))
		: Task.CompletedTask;

	private Task CommandAsync() => _detail is { } detail && _selected is { } row
		? OnOpenCommand.InvokeAsync(new PodSelection(detail.Name, row.Namespace.Length > 0 ? row.Namespace : null, null))
		: Task.CompletedTask;

	/// <summary>The details panel's facts, from the pod's JSON record exactly as far as it goes.</summary>
	internal static IReadOnlyList<Fact> DetailFacts(PodDetail detail)
	{
		List<Fact> facts =
		[
			new("Phase", detail.Phase.Length > 0 ? detail.Phase : "unknown"),
			new("Node", detail.Node.Length > 0 ? detail.Node : "not scheduled yet", mono: true),
			new("IP", detail.Ip.Length > 0 ? detail.Ip : "none yet", mono: true),
			new("QoS", detail.QosClass.Length > 0 ? detail.QosClass : "not stated"),
			new("Started", detail.StartTime.Length > 0 ? detail.StartTime : "not yet", mono: true),
			new("Restart policy", detail.RestartPolicy.Length > 0 ? detail.RestartPolicy : "not stated"),
		];
		foreach (PodCondition condition in detail.Conditions)
		{
			string note = condition.Reason.Length > 0
				? condition.Reason + (condition.Message.Length > 0 ? $": {condition.Message}" : "")
				: condition.Message;
			facts.Add(new Fact(condition.Type.Length > 0 ? condition.Type : "condition",
				[condition.Status.Length > 0 ? condition.Status : "?"], Note: note.Length > 0 ? note : null));
		}

		foreach (PodContainer container in detail.Containers)
		{
			string note = $"{(container.Ready ? "ready" : "not ready")}, {container.Restarts} restarts, {container.StateWord}" +
				(container.Detail.Length > 0 ? $": {container.Detail}" : "");
			facts.Add(new Fact(container.Name, [container.Image], Mono: true, Note: note));
		}

		return facts;
	}
}
