using Microsoft.Extensions.DependencyInjection;
using Moka.Network;
using Moka.Network.Components;
using Moka.Network.Settings;
using Moka.Network.Tool;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;

[assembly: MokatermPlugin(typeof(NetworkModule))]

namespace Moka.Network;

/// <summary>What the Network Toolkit plugin adds: the Network Toolkit app tool and its settings page.</summary>
public sealed class NetworkModule : IMokatermModule
{
	/// <summary>The app tool's id, which names its palette command and any shortcut bound to it.</summary>
	public const string ToolId = "network-toolkit";

	public ModuleInfo Info { get; } = new(
		"moka.network",
		"Network Toolkit",
		"DNS lookup, ping, TCP port check, traceroute, whois, Wake-on-LAN and a subnet calculator.",
		DisplayVersion.Of(typeof(NetworkModule).Assembly));

	public void ConfigureServices(IServiceCollection services)
	{
		services.AddAppTool(new AppToolDescriptor
		{
			Id = ToolId,
			Title = "Network Toolkit",
			Icon = NetworkIcons.Network,
			Description = "DNS lookup, ping, port check, traceroute, whois, Wake-on-LAN and subnets",
			Component = typeof(NetworkTool),
			Size = SessionToolSize.Wide,
		});

		// The page id, like the settings key, is plugin. and the plugin's id: the loader refuses a plugin that uses any other.
		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = NetworkSettings.SectionKey,
			Title = "Network Toolkit",
			Description = "The DNS tab's resolver and how long each kind of probe waits.",
			Group = SettingsPageGroup.Plugins,
			Icon = MokatermIcons.Plugin,
			Component = typeof(NetworkSettingsPage),
			Keywords = "network dns resolver cloudflare google quad9 ping traceroute port whois timeout wake lan",
		});
	}
}
