using Moka.Kubernetes.Server;
using Moka.Kubernetes.Settings;

namespace Moka.Kubernetes.Tests.Server;

/// <summary>
/// The commands the tool builds, word by word: the settings' reach into them (path, namespace, All namespaces) and
/// the quoting of names a hostile world could put in a listing, which is where free text meets the command line.
/// </summary>
public sealed class KubectlCommandTests
{
	[Fact]
	public void VersionCommand_AsksForJsonWithItsOwnRequestTimeout()
	{
		Assert.Equal("'kubectl' 'version' '-o' 'json' '--request-timeout=8s'", Kubectl.VersionCommand(new KubernetesSettings()));
	}

	[Fact]
	public void VersionCommand_UsesTheConfiguredPathAsOneWord()
	{
		KubernetesSettings settings = new() { KubectlPath = "/usr/local/bin/kubectl" };

		Assert.StartsWith("'/usr/local/bin/kubectl'", Kubectl.VersionCommand(settings), StringComparison.Ordinal);
	}

	[Fact]
	public void ClusterInfoCommand_IsThePlainRead()
	{
		Assert.Equal("'kubectl' 'cluster-info'", Kubectl.ClusterInfoCommand(new KubernetesSettings()));
	}

	[Fact]
	public void ContextCommand_ReadsTheKubeconfigAlone()
	{
		Assert.Equal("'kubectl' 'config' 'current-context'", Kubectl.ContextCommand(new KubernetesSettings()));
	}

	[Fact]
	public void NodesCommand_HasNoNamespaceToName()
	{
		Assert.Equal("'kubectl' 'get' 'nodes' '--no-headers'", Kubectl.NodesCommand(new KubernetesSettings()));
	}

	[Fact]
	public void PodsCommand_ByDefault_LeavesTheNamespaceToKubectl()
	{
		Assert.Equal("'kubectl' 'get' 'pods' '-o' 'wide' '--no-headers'", Kubectl.PodsCommand(new KubernetesSettings(), allNamespaces: false));
	}

	[Fact]
	public void PodsCommand_WithANamespaceSetting_NamesIt()
	{
		KubernetesSettings settings = new() { Namespace = "production" };

		Assert.Equal("'kubectl' '-n' 'production' 'get' 'pods' '-o' 'wide' '--no-headers'", Kubectl.PodsCommand(settings, allNamespaces: false));
	}

	[Fact]
	public void PodsCommand_AllNamespaces_WinsOverTheSetting()
	{
		KubernetesSettings settings = new() { Namespace = "production" };

		Assert.Equal("'kubectl' '-A' 'get' 'pods' '-o' 'wide' '--no-headers'", Kubectl.PodsCommand(settings, allNamespaces: true));
	}

	[Fact]
	public void PodCommand_QuotesThePodNameWhateverIsInIt()
	{
		KubernetesSettings settings = new() { Namespace = "production" };

		Assert.Equal("'kubectl' '-n' 'production' 'get' 'pod' 'web pod; rm -rf /' '-o' 'json'",
			Kubectl.PodCommand(settings, "web pod; rm -rf /", rowNamespace: null));
	}

	[Fact]
	public void PodCommand_TheRowsOwnNamespaceComesBeforeTheSettingsOne()
	{
		KubernetesSettings settings = new() { Namespace = "production" };

		Assert.Equal("'kubectl' '-n' 'monitoring' 'get' 'pod' 'prometheus-0' '-o' 'json'",
			Kubectl.PodCommand(settings, "prometheus-0", "monitoring"));
	}

	[Fact]
	public void LogsCommand_TailsWithTimestampsAndTheChosenContainer()
	{
		KubernetesSettings settings = new();

		Assert.Equal("'kubectl' 'logs' 'web-6799fc88d1-kx4t2' '-c' 'app' '--tail=500' '--timestamps'",
			Kubectl.LogsCommand(settings, "web-6799fc88d1-kx4t2", "app", rowNamespace: null));
	}

	[Fact]
	public void LogsCommand_WithoutAContainer_LeavesTheChoiceToKubectl()
	{
		Assert.Equal("'kubectl' 'logs' 'web-6799fc88d1-kx4t2' '--tail=500' '--timestamps'",
			Kubectl.LogsCommand(new KubernetesSettings(), "web-6799fc88d1-kx4t2", container: null, rowNamespace: null));
	}

	[Fact]
	public void LogsCommand_PreviousAndSinceTimeAreTheirOwnFlags()
	{
		Assert.Equal("'kubectl' 'logs' 'web' '--tail=500' '--timestamps' '-p' '--since-time=2026-11-01T09:00:03Z'",
			Kubectl.LogsCommand(new KubernetesSettings(), "web", container: null, rowNamespace: null, previous: true, sinceTime: "2026-11-01T09:00:03Z"));
	}

	[Fact]
	public void ExecCommand_RunsTheUsersWordsThroughOneShellAsOneQuotedWord()
	{
		Assert.Equal("'kubectl' 'exec' 'web' '-c' 'app' '--' 'sh' '-c' 'ls -l /data; cat $(which secret)'",
			Kubectl.ExecCommand(new KubernetesSettings(), "web", "app", rowNamespace: null, "ls -l /data; cat $(which secret)"));
	}

	[Fact]
	public void ExecCommand_APodNameWithQuotesSurvivesTheShell()
	{
		Assert.Equal("'kubectl' 'exec' 'pod'\\''s name' '--' 'sh' '-c' 'true'",
			Kubectl.ExecCommand(new KubernetesSettings(), "pod's name", container: null, rowNamespace: null, "true"));
	}

	[Fact]
	public void DeploymentsCommand_NamesTheNamespaceLikeThePodsListing()
	{
		KubernetesSettings settings = new() { Namespace = "production" };

		Assert.Equal("'kubectl' '-n' 'production' 'get' 'deployments' '--no-headers'", Kubectl.DeploymentsCommand(settings, allNamespaces: false));
		Assert.Equal("'kubectl' '-A' 'get' 'deployments' '--no-headers'", Kubectl.DeploymentsCommand(settings, allNamespaces: true));
	}

	[Fact]
	public void RolloutStatusCommand_WatchesNothingAndCarriesItsOwnTimeout()
	{
		Assert.Equal("'kubectl' 'rollout' 'status' 'deployment/web' '--watch=false' '--timeout=30s'",
			Kubectl.RolloutStatusCommand(new KubernetesSettings(), "web", rowNamespace: null, timeoutSeconds: 30));
	}

	[Fact]
	public void RolloutRestartAndUndo_NameTheDeploymentAndItsNamespace()
	{
		KubernetesSettings settings = new() { Namespace = "production" };

		Assert.Equal("'kubectl' '-n' 'production' 'rollout' 'restart' 'deployment/web'", Kubectl.RolloutRestartCommand(settings, "web", rowNamespace: null));
		Assert.Equal("'kubectl' '-n' 'monitoring' 'rollout' 'undo' 'deployment/prometheus'", Kubectl.RolloutUndoCommand(settings, "prometheus", "monitoring"));
	}

	[Fact]
	public void Program_AnEmptyPathMeansThePATH()
	{
		Assert.Equal("kubectl", Kubectl.Program(new KubernetesSettings()));
		Assert.Equal("/opt/kubectl", Kubectl.Program(new KubernetesSettings { KubectlPath = "/opt/kubectl" }));
	}
}
