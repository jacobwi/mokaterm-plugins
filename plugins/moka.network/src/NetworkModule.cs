using Microsoft.Extensions.DependencyInjection;
using Moka.Network;
using Moka.Network.Components;
using Moka.Network.Session;
using Moka.Network.Settings;
using Moka.Network.Targets;
using Moka.Network.Tool;
using Moka.Network.Whois;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;

[assembly: MokatermPlugin(typeof(NetworkModule))]

namespace Moka.Network;

/// <summary>What the Network Toolkit plugin adds: the Network Toolkit app tool, the session tool for SSH and its settings page.</summary>
public sealed class NetworkModule : IMokatermModule
{
	/// <summary>The app tool's id, which names its palette command and any shortcut bound to it.</summary>
	public const string ToolId = "network-toolkit";

	/// <summary>The session tool's id: the same name on every protocol it registers for, which names its palette command.</summary>
	public const string SessionToolId = "network";

	public ModuleInfo Info { get; } = new(
		"moka.network",
		"Network Toolkit",
		"DNS lookup, ping, TCP port check, traceroute, whois, Wake-on-LAN, a subnet calculator and, over SSH sessions, the same probes from the server.",
		DisplayVersion.Of(typeof(NetworkModule).Assembly));

	public void ConfigureServices(IServiceCollection services)
	{
		// One list of recent targets for every tab, and the RDAP client with IANA's bootstrap list, for the app run.
		services.AddSingleton<RecentTargets>();
		services.AddSingleton<IRdapTransport, HttpsRdapTransport>();
		services.AddSingleton<RdapClient>();

		services.AddAppTool(new AppToolDescriptor
		{
			Id = ToolId,
			Title = "Network Toolkit",
			Icon = NetworkIcons.Network,
			Description = "DNS lookup, ping, port check, traceroute, whois, Wake-on-LAN and subnets",
			Component = typeof(NetworkTool),
			Size = SessionToolSize.Wide,
		});

		// The same kinds of probe, from the other end of the session: resolve, listening ports, reach and capture on
		// the connected SSH server. It runs commands there, so the button waits for a connected session.
		services.AddSessionTool(new SessionToolDescriptor
		{
			ProtocolId = "ssh",
			Id = SessionToolId,
			Title = "Network from the server",
			Icon = NetworkIcons.Network,
			Component = typeof(NetworkSessionTool),
			ShowWhileDisconnected = false,
			Order = 900,
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
			Keywords = "network dns resolver cloudflare google quad9 ping traceroute port whois timeout wake lan session server capture tcpdump",
		});
	}
}
