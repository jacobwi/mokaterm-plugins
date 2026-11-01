using Microsoft.AspNetCore.Components;
using Moka.Kubernetes.Server;
using Moka.Kubernetes.Settings;
using Moka.Red.Core.Enums;
using Mokaterm.Abstractions.Interaction;
using Mokaterm.UI.Common.PluginUi;

namespace Moka.Kubernetes.Session;

/// <summary>
/// The cluster's deployments from <c>kubectl get deployments</c>, and the three rollout actions on the picked one:
/// Status, which reads where the rollout stands once, and Restart and Undo, which change the cluster. The host's
/// change-review contracts are the System and Docker pages' own, so a session tool confirms its changes the plain
/// way: a dialog naming the exact kubectl command about to run, then the command's answer on screen. After a change,
/// the tab says the listing under it was read before it.
/// </summary>
public sealed partial class RolloutsSection : SessionSectionBase
{
	private IReadOnlyList<DeploymentRow> _rows = [];
	private bool _listed;
	private bool _all;
	private bool _acting;
	private bool _stale;
	private string? _error;
	private DeploymentRow? _selected;
	private string? _actionTitle;
	private string? _actionCommand;
	private string? _actionOutput;
	private string? _actionError;

	[Inject]
	private IUserInteraction Interaction { get; set; } = default!;

	private bool Offline => Session is not null && !Connected;

	private int NotReady => _rows.Count(row => !row.FullyReady);

	private string Summary => !_listed
		? "Asking the cluster"
		: NotReady > 0
			? $"{Count(_rows.Count)} deployments, {Count(NotReady)} not fully ready"
			: $"{Count(_rows.Count)} deployments";

	private string ScopeNote => _all
		? "Every namespace, from kubectl's -A."
		: Settings.Namespace.Length > 0
			? $"Reading the {Settings.Namespace} namespace. Turn on All namespaces for every one."
			: "Reading only the namespace kubectl defaults to, which is the context's own. Turn on All namespaces for every one.";

	private string Columns => _all
		? "--mk8s-columns: 0.9fr 1.6fr 70px 80px 90px 70px"
		: "--mk8s-columns: 1.6fr 70px 80px 90px 70px";

	private MokaColor DotColor => _selected is { } row && !row.FullyReady ? MokaColor.Warning : MokaColor.Success;

	private string? PrimaryTitle => !Connected ? "The session is not connected." : null;

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
				Kubectl.DeploymentsCommand(settings, _all),
				settings.CommandTimeout,
				ServerRunner.ListingOutputBytes,
				cancellationToken: run.Token);
			Post(() =>
			{
				_listed = true;
				_stale = false;
				if (listing.Ok)
				{
					_rows = DeploymentListing.Parse(listing.Output, _all);
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

	private DeploymentRow? StillThere()
	{
		if (_selected is not { } picked)
		{
			return null;
		}

		DeploymentRow? again = _rows.FirstOrDefault(row => row.Name == picked.Name && row.Namespace == picked.Namespace);
		if (again is null)
		{
			CloseDetail();
		}

		return again;
	}

	private void Pick(DeploymentRow row)
	{
		_selected = row;
		_actionTitle = null;
		_actionCommand = null;
		_actionOutput = null;
		_actionError = null;
	}

	private void CloseDetail()
	{
		_selected = null;
		_actionTitle = null;
		_actionCommand = null;
		_actionOutput = null;
		_actionError = null;
	}

	/// <summary>Status asks once, with kubectl's own watch off, and its answer is the rollout's state in kubectl's words.</summary>
	private Task StatusAsync() => RunActionAsync(
		"Rollout status",
		"Where the rollout stands, read once.",
		confirm: false);

	/// <summary>Restart is a change: every pod of the deployment is replaced, a few at a time, so the dialog says that first.</summary>
	private Task RestartAsync() => RunActionAsync(
		"Rollout restart",
		$"Every pod of the deployment is replaced, a few at a time, each waiting to be ready before the next goes. {_selected?.Name} restarts now.",
		confirm: true);

	/// <summary>Undo is a change: the deployment goes back to its previous revision, whatever that was.</summary>
	private Task UndoAsync() => RunActionAsync(
		"Rollout undo",
		$"The deployment goes back to its previous revision. If the last rollout was a fix, this takes the fix back out. {_selected?.Name} rolls back now.",
		confirm: true);

	private async Task RunActionAsync(string title, string message, bool confirm)
	{
		if (_selected is not { } row || Runner is null)
		{
			return;
		}

		KubernetesSettings settings = Settings;
		string? space = row.Namespace.Length > 0 ? row.Namespace : null;
		string command = title switch
		{
			"Rollout status" => Kubectl.RolloutStatusCommand(settings, row.Name, space, settings.CommandTimeoutSeconds),
			"Rollout restart" => Kubectl.RolloutRestartCommand(settings, row.Name, space),
			_ => Kubectl.RolloutUndoCommand(settings, row.Name, space),
		};

		if (confirm)
		{
			ConfirmResult answer = await Interaction.ConfirmAsync(new ConfirmPrompt
			{
				Title = title,
				Message = message,
				ConfirmText = title["Rollout ".Length..],
				Details = command,
			});
			if (!answer.Confirmed)
			{
				return;
			}
		}

		if (Runner is not { } runner)
		{
			return;
		}

		_actionTitle = title;
		_actionCommand = command;
		_actionOutput = null;
		_actionError = null;
		_acting = true;
		StateHasChanged();
		CancellationTokenSource run = BeginRun();
		try
		{
			// The status ask carries kubectl's own --timeout, so the run gets that and ten seconds to spare.
			TimeSpan wait = title == "Rollout status" ? settings.CommandTimeout + TimeSpan.FromSeconds(10) : settings.CommandTimeout;
			ServerRun ended = await runner.RunAsync(command, wait, ServerRunner.RunOutputBytes, cancellationToken: run.Token);
			Post(() =>
			{
				_acting = false;
				if (ended.Ok)
				{
					_actionOutput = ended.Text.TrimEnd('\n');
					if (title != "Rollout status")
					{
						_stale = true;
					}
				}
				else
				{
					_actionError = KubectlErrors.Words(ended);
				}
			});
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			Post(() => _acting = false);
		}
		finally
		{
			EndRun(run);
		}
	}

	/// <summary>The detail panel's facts: the counts of the rollout as kubectl printed them.</summary>
	internal static IReadOnlyList<Fact> DetailFacts(DeploymentRow row)
	{
		List<Fact> facts =
		[
			new("Ready", row.Ready, mono: true),
			new("Up to date", row.UpToDate, mono: true),
			new("Available", row.Available, mono: true),
			new("Age", row.Age),
		];
		if (row.Namespace.Length > 0)
		{
			facts.Insert(0, new Fact("Namespace", row.Namespace, mono: true));
		}

		return facts;
	}
}
