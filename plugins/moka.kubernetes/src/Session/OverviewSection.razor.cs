using Moka.Kubernetes.Server;
using Moka.Kubernetes.Settings;
using Mokaterm.UI.Common.PluginUi;

namespace Moka.Kubernetes.Session;

/// <summary>
/// The cluster at a glance, every part of it one kubectl answer: whether the server has kubectl, the client and
/// server versions, the context its kubeconfig points at, the URLs <c>cluster-info</c> names, and the nodes. The tab
/// says plainly when there is no cluster behind the kubectl — no kubeconfig, a refused API server — rather than
/// drawing half an overview and calling it a cluster.
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
						? $"{_services.Count} services, {_nodes.Count} nodes"
						: "kubectl reached no cluster";

	private string? PrimaryTitle => !Connected
		? "The session is not connected."
		: Absent ? "This server has no kubectl."
		: null;

	/// <summary>The facts the overview shows, built from what the reads brought back.</summary>
	private IReadOnlyList<Fact> Facts
	{
		get
		{
			List<Fact> facts =
			[
				new("kubectl", _version.Client is { } client ? $"kubectl {client}" : "version not reported", mono: true),
			];
			facts.Add(_version.HasServer
				? new Fact("API server", _version.Server!, mono: true)
				: new Fact("API server", [], Note: "kubectl reached no API server, so the cluster's own version is not known."));
			facts.Add(new Fact("Context", _context is { Length: > 0 } context ? context : "not set", mono: true));
			facts.AddRange(_services.Select(service => new Fact(service.Name, service.Url, mono: true, copyable: true)));
			return facts;
		}
	}

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
			}

			Post(() =>
			{
				_hasKubectl = true;
				_version = version;
				_context = context;
				_services = services;
				_nodes = nodes;
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
