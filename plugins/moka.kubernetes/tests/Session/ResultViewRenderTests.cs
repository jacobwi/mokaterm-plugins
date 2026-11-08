using Moka.Kubernetes.Server;
using Moka.Kubernetes.Session;
using Moka.Kubernetes.Tests.Server;
using Mokaterm.Plugin.TestHost;

namespace Moka.Kubernetes.Tests.Session;

/// <summary>
/// What each tab draws once kubectl answered, rendered from the fixtures' answers through the plugin's own readers: the
/// static render never runs a read, so the result views are rendered with what a read would hand them.
/// </summary>
public sealed class ResultViewRenderTests
{
	[Fact]
	public async Task ClusterSummary_ShowsTheFiguresThePodStatesTheServicesAndTheNodes()
	{
		string html = await StaticRender.RenderAsync<ClusterSummary>(new Dictionary<string, object?>
		{
			["Version"] = KubectlVersion.Parse(Fixtures.VersionWithServer),
			["Context"] = "production",
			["Services"] = ClusterInfoText.Parse(Fixtures.ClusterInfo),
			["Nodes"] = NodeListing.Parse(Fixtures.Nodes),
			["Pods"] = PodListing.Parse(Fixtures.PodsAllNamespaces, hasNamespace: true),
		});

		Assert.Contains("v1.30.4", html, StringComparison.Ordinal);
		Assert.Contains("v1.31.1", html, StringComparison.Ordinal);
		Assert.Contains("production", html, StringComparison.Ordinal);
		Assert.Contains("2/3", html, StringComparison.Ordinal);
		Assert.Contains("1 not ready", html, StringComparison.Ordinal);
		Assert.Contains("moka-stat--tile", html, StringComparison.Ordinal);
		Assert.Contains("moka-donutchart", html, StringComparison.Ordinal);
		Assert.Contains("Running 2", html, StringComparison.Ordinal);
		Assert.Contains("Pending 1", html, StringComparison.Ordinal);
		Assert.Contains("1 to look at", html, StringComparison.Ordinal);
		Assert.Contains("https://192.0.2.10:6443", html, StringComparison.Ordinal);
		Assert.Contains("Copy the Kubernetes control plane address", html, StringComparison.Ordinal);
		Assert.Contains("node-c.example", html, StringComparison.Ordinal);
		Assert.Contains("NotReady", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"The cluster&#x27;s nodes\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ClusterSummary_WithoutAnApiServer_SaysSoAndAsksNothingElse()
	{
		string html = await StaticRender.RenderAsync<ClusterSummary>(new Dictionary<string, object?>
		{
			["Version"] = KubectlVersion.Parse(Fixtures.VersionClientOnly),
		});

		Assert.Contains("not reached", html, StringComparison.Ordinal);
		Assert.Contains("kubectl reached no API server", html, StringComparison.Ordinal);
		Assert.Contains("not set", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Nodes ready", html, StringComparison.Ordinal);
		Assert.DoesNotContain("moka-table", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ClusterSummary_WhenThePodsWereRefused_SaysWhyInThePodsCard()
	{
		string html = await StaticRender.RenderAsync<ClusterSummary>(new Dictionary<string, object?>
		{
			["Version"] = KubectlVersion.Parse(Fixtures.VersionWithServer),
			["Nodes"] = NodeListing.Parse(Fixtures.Nodes),
			["PodsProblem"] = "kubectl may not list pods at the cluster scope.",
		});

		Assert.Contains("kubectl would not list the pods of every namespace", html, StringComparison.Ordinal);
		Assert.Contains("not listed", html, StringComparison.Ordinal);
		Assert.DoesNotContain("moka-donutchart-ring", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task PodTable_ListsEveryPodWithItsStateDot()
	{
		string html = await StaticRender.RenderAsync<PodTable>(new Dictionary<string, object?>
		{
			["Rows"] = PodListing.Parse(Fixtures.PodsWide, hasNamespace: false),
		});

		Assert.Contains("worker-7d9f4c8b6c-q2r7v", html, StringComparison.Ordinal);
		Assert.Contains("CrashLoopBackOff", html, StringComparison.Ordinal);
		Assert.Contains("0/1", html, StringComparison.Ordinal);
		Assert.Contains("node-b.example", html, StringComparison.Ordinal);
		Assert.Contains("moka-status-dot", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"The cluster&#x27;s pods\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task PodTable_WithNothingListed_SaysWhichNamespaceWasAsked()
	{
		string html = await StaticRender.RenderAsync<PodTable>(new Dictionary<string, object?>
		{
			["Rows"] = PodListing.Parse(Fixtures.PodsHeaderOnly, hasNamespace: false),
		});

		Assert.Contains("No pods listed", html, StringComparison.Ordinal);
		Assert.Contains("turn on All namespaces for every one", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task PodDetailView_ShowsTheRecordItsConditionsAndEachContainer()
	{
		PodDetail detail = PodDetail.Parse(Fixtures.PodJson)!;

		string html = await StaticRender.RenderAsync<PodDetailView>(new Dictionary<string, object?> { ["Detail"] = detail });

		Assert.Contains("Burstable", html, StringComparison.Ordinal);
		Assert.Contains("10.244.0.15", html, StringComparison.Ordinal);
		Assert.Contains("Copy the pod&#x27;s IP", html, StringComparison.Ordinal);
		Assert.Contains("ContainersNotReady: containers with unready status: [app]", html, StringComparison.Ordinal);
		Assert.Contains("registry.example/web:1.4.2", html, StringComparison.Ordinal);
		Assert.Contains("Copy the app image", html, StringComparison.Ordinal);
		Assert.Contains("waiting", html, StringComparison.Ordinal);
		Assert.Contains("running", html, StringComparison.Ordinal);
		Assert.Contains("CrashLoopBackOff", html, StringComparison.Ordinal);
		Assert.Contains("moka-data-list", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task PodDetailView_ForAPodStillScheduling_SaysWhatIsNotThereYet()
	{
		PodDetail detail = PodDetail.Parse(Fixtures.PodJsonPending)!;

		string html = await StaticRender.RenderAsync<PodDetailView>(new Dictionary<string, object?> { ["Detail"] = detail });

		Assert.Contains("not scheduled yet", html, StringComparison.Ordinal);
		Assert.Contains("none yet", html, StringComparison.Ordinal);
		Assert.Contains("Unschedulable", html, StringComparison.Ordinal);
		Assert.Contains("prom/prometheus:v2.54.1", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DeploymentTable_DrawsEachRolloutsReadyShare()
	{
		string html = await StaticRender.RenderAsync<DeploymentTable>(new Dictionary<string, object?>
		{
			["Rows"] = DeploymentListing.Parse(Fixtures.Deployments, hasNamespace: false),
		});

		Assert.Contains("worker", html, StringComparison.Ordinal);
		Assert.Contains("1/4", html, StringComparison.Ordinal);
		Assert.Contains("role=\"progressbar\"", html, StringComparison.Ordinal);
		Assert.Contains("1/4 pods ready", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RolloutDetailView_ChainsTheReplicaCounts()
	{
		DeploymentRow worker = DeploymentListing.Parse(Fixtures.Deployments, hasNamespace: false)[1];

		string html = await StaticRender.RenderAsync<RolloutDetailView>(new Dictionary<string, object?> { ["Row"] = worker });

		Assert.Contains("moka-chain-check", html, StringComparison.Ordinal);
		Assert.Contains("4 pods", html, StringComparison.Ordinal);
		Assert.Contains("4 of 4", html, StringComparison.Ordinal);
		Assert.Contains("1 of 4", html, StringComparison.Ordinal);
		Assert.Contains("12d", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task PodLogView_DrawsEachLineAfterItsLocalTime()
	{
		LogLines lines = new();
		lines.Append(Fixtures.LogTail);
		lines.Append(Fixtures.LogLineWithoutTime + "\n");
		IReadOnlyList<PodLogLine> rows = lines.Snapshot();

		string html = await StaticRender.RenderAsync<PodLogView>(new Dictionary<string, object?>
		{
			["Lines"] = rows,
			["Title"] = "worker (app)",
		});

		Assert.Contains("moka-terminal", html, StringComparison.Ordinal);
		Assert.Contains("worker (app)", html, StringComparison.Ordinal);
		Assert.Contains("starting worker version 1.4.2", html, StringComparison.Ordinal);
		Assert.Contains(PodLogView.Stamp(rows[0])!, html, StringComparison.Ordinal);
		Assert.Contains(Fixtures.LogLineWithoutTime, html, StringComparison.Ordinal);
		Assert.Null(PodLogView.Stamp(rows[^1]));
	}

	[Fact]
	public async Task CommandOutputView_ShowsTheCommandItsOutputAndHowItEnded()
	{
		string html = await StaticRender.RenderAsync<CommandOutputView>(new Dictionary<string, object?>
		{
			["Title"] = "web (app)",
			["Command"] = "ls -l /data",
			["Output"] = "total 0\n",
			["Error"] = "ls: cannot access '/data': No such file or directory\n",
			["ExitCode"] = 2,
			["Duration"] = TimeSpan.FromMilliseconds(1500),
		});

		Assert.Contains("Exit 2", html, StringComparison.Ordinal);
		Assert.Contains("ls -l /data", html, StringComparison.Ordinal);
		Assert.Contains("total 0", html, StringComparison.Ordinal);
		Assert.Contains("No such file or directory", html, StringComparison.Ordinal);
		Assert.Contains("var(--moka-color-error)", html, StringComparison.Ordinal);
		Assert.Contains("Copy output", html, StringComparison.Ordinal);
		Assert.DoesNotContain("moka-terminal-copy", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task CommandOutputView_PastTheDisplayLimit_SaysCopyTakesAll()
	{
		string html = await StaticRender.RenderAsync<CommandOutputView>(new Dictionary<string, object?>
		{
			["Output"] = new string('x', 50) + "\n" + new string('y', 50),
			["DisplayChars"] = 20,
		});

		Assert.Contains("Showing the first 20 characters of 101", html, StringComparison.Ordinal);
		Assert.DoesNotContain("yyyy", html, StringComparison.Ordinal);
	}
}
