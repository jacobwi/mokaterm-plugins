using Moka.Kubernetes.Components;
using Mokaterm.UI.Common.Components;
using Mokaterm.UI.Common.ToolPages;

namespace Moka.Kubernetes.Session;

/// <summary>
/// Kubernetes as a session tool for SSH: the cluster's overview, its pods and their logs, a one-shot command runner
/// and rollouts, each read through kubectl on the connected server and shown from that side of the link. It needs
/// the session connected, so the shell hides the button until it is.
/// </summary>
public sealed partial class KubernetesSessionTool : SessionViewBase
{
	private const string OverviewId = "overview";

	private const string PodsId = "pods";

	private const string LogsId = "logs";

	private const string CommandId = "command";

	private const string RolloutsId = "rollouts";

	private static readonly IReadOnlyList<ToolNavEntry> Entries =
	[
		new(OverviewId, "Overview", KubernetesIcons.Overview),
		new(PodsId, "Pods", KubernetesIcons.Pod),
		new(LogsId, "Logs", KubernetesIcons.Logs),
		new(CommandId, "Command", KubernetesIcons.Command),
		new(RolloutsId, "Rollouts", KubernetesIcons.Rollout),
	];

	private string _current = OverviewId;

	private LogsSection? _logs;

	private ExecSection? _command;

	private void Show(string id) => _current = id;

	/// <summary>The pods tab hands a pod over for its logs: the log tab lands on it and asks at once.</summary>
	private void OpenLogs(PodSelection selection)
	{
		_current = LogsId;
		_logs?.Show(selection);
	}

	/// <summary>The pods tab hands a pod over for the command runner, which fills its pod and container fields.</summary>
	private void OpenCommand(PodSelection selection)
	{
		_current = CommandId;
		_command?.Show(selection);
	}
}
