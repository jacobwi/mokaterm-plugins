using Moka.Network.Lookup;
using Mokaterm.UI.Common.Components;

namespace Moka.Network.Settings;

/// <summary>The plugin's settings page, in the Plugins group, built from the shared settings fields.</summary>
public sealed partial class NetworkSettingsPage : SettingsSectionBase<NetworkSettings>
{
	private static readonly IReadOnlyList<EnumOption<DnsResolverKind>> Resolvers =
	[
		new(DnsResolverKind.System, "This machine"),
		new(DnsResolverKind.Cloudflare, "Cloudflare"),
		new(DnsResolverKind.Google, "Google"),
		new(DnsResolverKind.Quad9, "Quad9"),
		new(DnsResolverKind.Custom, "Custom"),
	];

	private Task SaveAsync(Func<NetworkSettings, NetworkSettings> change) => UpdateAsync(settings => change(settings).Clamped());
}
