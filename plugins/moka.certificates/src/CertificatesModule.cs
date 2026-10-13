using Microsoft.Extensions.DependencyInjection;
using Moka.Certificates;
using Moka.Certificates.Components;
using Moka.Certificates.ServerFiles;
using Moka.Certificates.Settings;
using Moka.Certificates.SystemPage;
using Moka.Certificates.Tool;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;

[assembly: MokatermPlugin(typeof(CertificatesModule))]

namespace Moka.Certificates;

/// <summary>What the Certificates plugin adds: the Certificates app tool, a System view page and its settings page.</summary>
public sealed class CertificatesModule : IMokatermModule
{
	/// <summary>The app tool's id, which names its palette command and any shortcut bound to it.</summary>
	public const string ToolId = "certificates";

	/// <summary>The System view page's id. Not "certificates", which a built-in page could take one day.</summary>
	public const string SystemPageId = "server-certificates";

	public ModuleInfo Info { get; } = new(
		"moka.certificates",
		"Certificates",
		"Inspect, generate, convert, sign and encrypt with certificates and keys; check a server's TLS chain; find the certificates a server uses.",
		DisplayVersion.Of(typeof(CertificatesModule).Assembly));

	public void ConfigureServices(IServiceCollection services)
	{
		services.AddAppTool(new AppToolDescriptor
		{
			Id = ToolId,
			Title = "Certificates",
			Icon = CertificateIcons.Certificate,
			Description = "Inspect, generate, convert, sign and encrypt with certificates and keys, and check a server's chain",
			Component = typeof(CertificatesTool),
			Size = SessionToolSize.Wide,
		});

		// After the view's Security page (1200).
		services.AddSystemPage(new SystemPageDescriptor
		{
			Id = SystemPageId,
			Title = "Certificates",
			Icon = CertificateIcons.Certificate,
			Component = typeof(ServerCertificatesPage),
			Order = 1250,
			Commands = ServerCertificateCommands.Probed,
		});

		// The page id, like the settings key, is plugin. and the plugin's id: the loader refuses a plugin that uses any other.
		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = CertificateSettings.SectionKey,
			Title = "Certificates",
			Description = "What new keys and certificates default to, when a certificate counts as expiring, the server check's time limit and extra paths for the System page.",
			Group = SettingsPageGroup.Plugins,
			Icon = MokatermIcons.Plugin,
			Component = typeof(CertificateSettingsPage),
			Keywords = "certificate key rsa ecdsa curve validity pfx expiring tls server check certbot nginx apache",
		});
	}
}
