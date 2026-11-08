using Moka.Kubernetes.Server;
using Moka.Kubernetes.Settings;

namespace Moka.Kubernetes.Session;

/// <summary>
/// The cluster at a glance, every part of it one kubectl answer: whether the server has kubectl, the client and
/// server versions, the context its kubeconfig points at, the URLs <c>cluster-info</c> names, the nodes, and every
/// namespace's pods for their states. The tab says plainly when there is no cluster behind the kubectl (no kubeconfig,
/// a refused API server) rather than drawing half an overview and calling it a cluster.
/// </summary>
public sealed partial class OverviewSection : SessionSectionBase
{
	private bool _probed;
	private bool _hasKubectl;
	private bool _read;
	private string? _error;
	private KubectlVersion _version = new(null, null);
	private string? _context;
	private IReadOnlyList<ClusterService> _services = [];
	private IReadOnlyList<NodeRow> _nodes = [];
	private IReadOnlyList<PodRow>? _pods;
	private string? _podsProblem;

	private bool Offline => Session is not null && !Connected;

	private bool Absent => _probed && !_hasKubectl;

	private string Summary => !_probed
		? "Asking the server what it has"
		: Absent
			? "no kubectl on this server"
			: !_read
				? "Asking the cluster"
				: _error is not null && _version.HasServer is false
					? "kubectl answered with an error"
					: _version.HasServer
						? $"{_services.Count} services, {_nodes.Count} nodes{(_pods is { } pods ? $", {Count(pods.Count)} pods" : "")}"
						: "kubectl reached no cluster";

	private string? PrimaryTitle => !Connected
		? "The session is not connected."
		: Absent ? "This server has no kubectl."
		: null;

	protected override void OnSessionChange()
	{
		if (Connected && !_probed)
		{
			_ = ReadAsync();
		}
	}

	protected override void OnAfterRender(bool firstRender)
	{
		if (firstRender && Connected && !_probed)
		{
			_ = ReadAsync();
		}
	}

	private async Task PrimaryAsync()
	{
		if (Running)
		{
			Stop();
			return;
		}

		await ReadAsync();
	}

	private async Task ReadAsync()
	{
		if (Runner is not { } runner)
		{
			return;
		}

		_error = null;
		_probed = true;
		CancellationTokenSource run = BeginRun();
		StateHasChanged();
		try
		{
			if (!await runner.HasKubectlAsync(Settings.KubectlPath, run.Token).ConfigureAwait(true))
			{
				Post(() =>
				{
					_hasKubectl = false;
					_read = true;
					_nodes = [];
					_services = [];
					_pods = null;
					_podsProblem = null;
				});
				return;
			}

			KubernetesSettings settings = Settings;
			ServerRun versionRun = await runner.RunAsync(Kubectl.VersionCommand(settings), settings.CommandTimeout, cancellationToken: run.Token);
			KubectlVersion version = versionRun.NeverRan || versionRun.Broke ? new(null, null) : KubectlVersion.Parse(versionRun.Output);
			string? problem = versionRun.Ok ? null : KubectlErrors.Words(versionRun);

			// The context is the kubeconfig's own answer, read without the cluster: it is worth showing even when
			// the cluster itself cannot be reached.
			ServerRun contextRun = await runner.RunAsync(Kubectl.ContextCommand(settings), settings.CommandTimeout, cancellationToken: run.Token);
			string? context = contextRun.Ok ? contextRun.Output.Trim() : null;

			IReadOnlyList<ClusterService> services = [];
			IReadOnlyList<NodeRow> nodes = [];
			IReadOnlyList<PodRow>? pods = null;
			string? podsProblem = null;
			if (version.HasServer)
			{
				// Only a cluster that answered its version is worth asking for anything else.
				ServerRun infoRun = await runner.RunAsync(Kubectl.ClusterInfoCommand(settings), settings.CommandTimeout, cancellationToken: run.Token);
				if (infoRun.Ok)
				{
					services = ClusterInfoText.Parse(infoRun.Output);
				}
				else
				{
					problem = problem is null ? KubectlErrors.Words(infoRun) : problem;
				}

				ServerRun nodesRun = await runner.RunAsync(Kubectl.NodesCommand(settings), settings.CommandTimeout, cancellationToken: run.Token);
				if (nodesRun.Ok)
				{
					nodes = NodeListing.Parse(nodesRun.Output);
				}

				// Every namespace, because the states are the cluster's. An account kept to one namespace is refused
				// this, which the pods card says rather than the tab's error line: the rest of the read stands.
				ServerRun podsRun = await runner.RunAsync(
					Kubectl.PodsCommand(settings, allNamespaces: true),
					settings.CommandTimeout,
					ServerRunner.ListingOutputBytes,
					cancellationToken: run.Token);
				if (podsRun.Ok)
				{
					pods = PodListing.Parse(podsRun.Output, hasNamespace: true);
				}
				else
				{
					podsProblem = KubectlErrors.Words(podsRun);
				}
			}

			Post(() =>
			{
				_hasKubectl = true;
				_version = version;
				_context = context;
				_services = services;
				_nodes = nodes;
				_pods = pods;
				_podsProblem = podsProblem;
				_error = problem;
				_read = true;
			});
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			// Stopped: what was read so far stays.
		}
		finally
		{
			EndRun(run);
		}
	}
}
