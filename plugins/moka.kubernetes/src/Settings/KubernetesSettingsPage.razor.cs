using Mokaterm.UI.Common.Components;

namespace Moka.Kubernetes.Settings;

/// <summary>The plugin's settings page, in the Plugins group, built from the shared settings fields.</summary>
public sealed partial class KubernetesSettingsPage : SettingsSectionBase<KubernetesSettings>
{
	private Task SaveAsync(Func<KubernetesSettings, KubernetesSettings> change) => UpdateAsync(settings => change(settings).Clamped());
}
