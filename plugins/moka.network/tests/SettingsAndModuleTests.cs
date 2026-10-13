using Microsoft.Extensions.DependencyInjection;
using Moka.Network.Lookup;
using Moka.Network.Settings;
using Moka.Network.Tool;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Contributions;

namespace Moka.Network.Tests;

public sealed class SettingsAndModuleTests
{
	[Fact]
	public void Clamped_HoldsEveryValueToItsRange()
	{
		NetworkSettings wild = new()
		{
			Resolver = (DnsResolverKind)42,
			CustomResolver = "not an address",
			DnsTimeoutSeconds = 0,
			ConnectTimeoutSeconds = 999,
			WhoisTimeoutSeconds = -5,
			PingTimeoutMilliseconds = int.MaxValue,
		};

		NetworkSettings clamped = wild.Clamped();

		Assert.Equal(DnsResolverKind.System, clamped.Resolver);
		Assert.Equal("", clamped.CustomResolver);
		Assert.Equal(NetworkSettings.MinDnsTimeoutSeconds, clamped.DnsTimeoutSeconds);
		Assert.Equal(NetworkSettings.MaxConnectTimeoutSeconds, clamped.ConnectTimeoutSeconds);
		Assert.Equal(NetworkSettings.MinWhoisTimeoutSeconds, clamped.WhoisTimeoutSeconds);
		Assert.Equal(NetworkSettings.MaxPingTimeoutMilliseconds, clamped.PingTimeoutMilliseconds);
		Assert.Equal(new NetworkSettings(), new NetworkSettings().Clamped());
	}

	[Fact]
	public void Clamped_KeepsCustomPickedWhileTheAddressIsStillToCome()
	{
		NetworkSettings settings = new NetworkSettings { Resolver = DnsResolverKind.Custom, CustomResolver = " 10.0.0.53:5353 " }.Clamped();

		Assert.Equal(DnsResolverKind.Custom, settings.Resolver);
		Assert.Equal("10.0.0.53:5353", settings.CustomResolver);
		Assert.Equal(DnsResolverKind.Custom, (settings with { CustomResolver = "" }).Clamped().Resolver);
	}

	[Fact]
	public void SectionKey_IsUnderThePluginsOwnName() => Assert.Equal("plugin.moka.network", NetworkSettings.SectionKey);

	[Fact]
	public void Module_RegistersTheToolAndTheSettings()
	{
		ServiceCollection services = new();

		new NetworkModule().ConfigureServices(services);
		using ServiceProvider provider = services.BuildServiceProvider();

		AppToolDescriptor tool = Assert.Single(provider.GetServices<AppToolDescriptor>());
		Assert.Equal(NetworkModule.ToolId, tool.Id);
		Assert.True(UiContributionServiceCollectionExtensions.IsToolId(tool.Id));
		Assert.Equal(typeof(NetworkTool), tool.Component);
		Assert.Equal(SessionToolSize.Wide, tool.Size);

		SettingsPageDescriptor settings = Assert.Single(provider.GetServices<SettingsPageDescriptor>());
		Assert.Equal(NetworkSettings.SectionKey, settings.Id);
		Assert.Equal(SettingsPageGroup.Plugins, settings.Group);
		Assert.Equal(typeof(NetworkSettingsPage), settings.Component);
		Assert.Equal(NetworkSettings.SectionKey, Assert.Single(provider.GetServices<SettingsSectionType>()).Key);
		Assert.Equal("moka.network", new NetworkModule().Info.Id);
	}
}
