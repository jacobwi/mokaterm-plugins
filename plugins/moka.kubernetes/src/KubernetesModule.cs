using Microsoft.Extensions.DependencyInjection;
using Moka.Kubernetes;
using Moka.Kubernetes.Components;
using Moka.Kubernetes.Session;
using Moka.Kubernetes.Settings;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;

[assembly: MokatermPlugin(typeof(KubernetesModule))]

namespace Moka.Kubernetes;

/// <summary>
/// What the Kubernetes plugin adds: one session tool for SSH that reads the cluster the session's server reaches
/// through kubectl, and its settings page. There is no app tool on purpose: kubectl lives on servers, and a cluster
/// read from this computer's own kubectl, where there may be none, is not a story worth telling.
/// </summary>
public sealed class KubernetesModule : IMokatermModule
{
	/// <summary>The session tool's id: the same name on every protocol it registers for, which names its palette command.</summary>
	public const string SessionToolId = "kubernetes";

	public ModuleInfo Info { get; } = new(
		"moka.kubernetes",
		"Kubernetes",
		"Pods and their logs, a one-shot command runner and rollouts on the cluster the session's server reaches through kubectl.",
		DisplayVersion.Of(typeof(KubernetesModule).Assembly));

	public void ConfigureServices(IServiceCollection services)
	{
		// One tool, on SSH, while the session is connected: every one of its tabs runs kubectl on the server through
		// the session's command feature, so a disconnected session has nothing to ask.
		services.AddSessionTool(new SessionToolDescriptor
		{
			ProtocolId = "ssh",
			Id = SessionToolId,
			Title = "Kubernetes from the server",
			Icon = KubernetesIcons.Kubernetes,
			Component = typeof(KubernetesSessionTool),
			ShowWhileDisconnected = false,
			Order = 910,
			Size = SessionToolSize.Wide,
		});

		// The page id, like the settings key, is plugin. and the plugin's id: the loader refuses a plugin that uses any other.
		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = KubernetesSettings.SectionKey,
			Title = "Kubernetes",
			Description = "The kubectl the session tool calls on the server, how long one command gets, and which namespace it prefers.",
			Group = SettingsPageGroup.Plugins,
			Icon = MokatermIcons.Plugin,
			Component = typeof(KubernetesSettingsPage),
			Keywords = "kubernetes kubectl pods cluster context kubeconfig namespace containers deployments rollout restart undo logs exec timeout server session",
		});
	}
}
