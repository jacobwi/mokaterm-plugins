using System.Globalization;
using Moka.Kubernetes.Settings;

namespace Moka.Kubernetes.Server;

/// <summary>
/// Every kubectl call the session tool makes, built from words this plugin checked itself and quoted as one line with
/// <see cref="ServerQuote"/>, so a pod or deployment name with spaces or shell characters in it is still one word to
/// the shell. Every call is one command to its end: no channel is held open, which is why follow is a poll and the
/// command runner is one-shot. The namespace each call names comes from the row it is about when there is one, then
/// from the All-namespaces switch, then from the namespace setting, then from kubectl's own default, in that order.
/// </summary>
public static class Kubectl
{
	/// <summary>The time kubectl itself gives one API request, which bounds the version ask independently of the run timeout.</summary>
	public const int RequestTimeoutSeconds = 8;

	/// <summary>How many lines of a log the tail fetch asks for, which the follow polls then build on.</summary>
	public const int LogTailLines = 500;

	/// <summary>The kubectl word every call starts with: the setting's path, or <c>kubectl</c> from the server's PATH.</summary>
	public static string Program(KubernetesSettings settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		return settings.KubectlPath.Length > 0 ? settings.KubectlPath : "kubectl";
	}

	/// <summary>
	/// The namespace words for a listing: <c>-A</c> when every namespace was asked for, <c>-n</c> and the setting's
	/// name when one was set, nothing when kubectl's own default is the honest answer.
	/// </summary>
	public static IReadOnlyList<string> ListingScope(KubernetesSettings settings, bool allNamespaces)
	{
		ArgumentNullException.ThrowIfNull(settings);
		if (allNamespaces)
		{
			return ["-A"];
		}

		return settings.Namespace.Length > 0 ? ["-n", settings.Namespace] : [];
	}

	/// <summary>
	/// The namespace words for an action on one row: the namespace the row itself was listed under comes first, since
	/// only it says which of two same-named pods in different namespaces is meant.
	/// </summary>
	public static IReadOnlyList<string> RowScope(KubernetesSettings settings, string? rowNamespace)
	{
		ArgumentNullException.ThrowIfNull(settings);
		string name = rowNamespace?.Trim() ?? "";
		return name.Length > 0 ? ["-n", name] : ListingScope(settings, allNamespaces: false);
	}

	/// <summary><c>version -o json --request-timeout=8s</c>: the client and server versions, or the client's alone.</summary>
	public static string VersionCommand(KubernetesSettings settings) => ServerQuote.Line(
		[Program(settings), "version", "-o", "json", $"--request-timeout={RequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture)}s"]);

	/// <summary><c>cluster-info</c>: the control plane and add-on URLs the API server reports.</summary>
	public static string ClusterInfoCommand(KubernetesSettings settings) =>
		ServerQuote.Line([Program(settings), "cluster-info"]);

	/// <summary><c>config current-context</c>: which context the server's kubeconfig points at, read from it alone.</summary>
	public static string ContextCommand(KubernetesSettings settings) =>
		ServerQuote.Line([Program(settings), "config", "current-context"]);

	/// <summary><c>get nodes --no-headers</c>: the cluster's nodes with their status and version.</summary>
	public static string NodesCommand(KubernetesSettings settings) =>
		ServerQuote.Line([Program(settings), "get", "nodes", "--no-headers"]);

	/// <summary><c>get pods -o wide --no-headers</c>: the pods, with their node and IP because of the wide output.</summary>
	public static string PodsCommand(KubernetesSettings settings, bool allNamespaces) => ServerQuote.Line(
		[Program(settings), .. ListingScope(settings, allNamespaces), "get", "pods", "-o", "wide", "--no-headers"]);

	/// <summary><c>get deployments --no-headers</c>: the deployments and how many of their pods stand ready.</summary>
	public static string DeploymentsCommand(KubernetesSettings settings, bool allNamespaces) => ServerQuote.Line(
		[Program(settings), .. ListingScope(settings, allNamespaces), "get", "deployments", "--no-headers"]);

	/// <summary><c>get pod NAME -o json</c>: one pod's whole record, for the details panel. The name is always quoted.</summary>
	public static string PodCommand(KubernetesSettings settings, string pod, string? rowNamespace) => ServerQuote.Line(
		[Program(settings), .. RowScope(settings, rowNamespace), "get", "pod", pod, "-o", "json"]);

	/// <summary>
	/// <c>logs POD -c CONTAINER --tail=500 --timestamps</c>, with <c>-p</c> for the previous container's logs and
	/// <c>--since-time</c> for the follow poll's ask for what is new.
	/// </summary>
	public static string LogsCommand(
		KubernetesSettings settings,
		string pod,
		string? container,
		string? rowNamespace,
		bool previous = false,
		string? sinceTime = null)
	{
		List<string> words = [Program(settings), .. RowScope(settings, rowNamespace), "logs", pod];
		if (container is { Length: > 0 } name)
		{
			words.Add("-c");
			words.Add(name);
		}

		words.Add($"--tail={LogTailLines.ToString(CultureInfo.InvariantCulture)}");
		words.Add("--timestamps");
		if (previous)
		{
			words.Add("-p");
		}

		if (sinceTime is { Length: > 0 } since)
		{
			words.Add($"--since-time={since}");
		}

		return ServerQuote.Line(words);
	}

	/// <summary>
	/// <c>exec POD -c CONTAINER -- sh -c COMMAND</c>: the user's command as one quoted word to a shell inside the
	/// container, run to its end and captured. There is no terminal on the other side of this, which is the honest
	/// limit of the command runner.
	/// </summary>
	public static string ExecCommand(KubernetesSettings settings, string pod, string? container, string? rowNamespace, string command)
	{
		List<string> words = [Program(settings), .. RowScope(settings, rowNamespace), "exec", pod];
		if (container is { Length: > 0 } name)
		{
			words.Add("-c");
			words.Add(name);
		}

		words.Add("--");
		words.Add("sh");
		words.Add("-c");
		words.Add(command);
		return ServerQuote.Line(words);
	}

	/// <summary><c>rollout status deployment/NAME --watch=false --timeout=Ns</c>: where the rollout stands, once.</summary>
	public static string RolloutStatusCommand(KubernetesSettings settings, string name, string? rowNamespace, int timeoutSeconds) =>
		ServerQuote.Line([Program(settings), .. RowScope(settings, rowNamespace), "rollout", "status",
			$"deployment/{name}", "--watch=false",
			$"--timeout={timeoutSeconds.ToString(CultureInfo.InvariantCulture)}s"]);

	/// <summary><c>rollout restart deployment/NAME</c>: a change, confirmed before it runs.</summary>
	public static string RolloutRestartCommand(KubernetesSettings settings, string name, string? rowNamespace) =>
		ServerQuote.Line([Program(settings), .. RowScope(settings, rowNamespace), "rollout", "restart", $"deployment/{name}"]);

	/// <summary><c>rollout undo deployment/NAME</c>: a change, confirmed before it runs.</summary>
	public static string RolloutUndoCommand(KubernetesSettings settings, string name, string? rowNamespace) =>
		ServerQuote.Line([Program(settings), .. RowScope(settings, rowNamespace), "rollout", "undo", $"deployment/{name}"]);
}
