using Microsoft.Extensions.DependencyInjection;
using Moka.DevTools.Tool;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.ToolPages;

namespace Moka.DevTools.Tests;

public sealed class ModuleTests
{
	[Fact]
	public void Module_RegistersOneWideAppTool()
	{
		ServiceCollection services = new();

		new DevToolsModule().ConfigureServices(services);
		using ServiceProvider provider = services.BuildServiceProvider();

		AppToolDescriptor tool = Assert.Single(provider.GetServices<AppToolDescriptor>());
		Assert.Equal("devtools", tool.Id);
		Assert.Equal("Developer Utilities", tool.Title);
		Assert.Equal(typeof(DevToolsTool), tool.Component);
		Assert.True(typeof(AppToolBase).IsAssignableFrom(tool.Component));
		Assert.Equal(SessionToolSize.Wide, tool.Size);
		Assert.Empty(provider.GetServices<SettingsPageDescriptor>());
	}

	[Fact]
	public void Module_IdIsThePluginId() => Assert.Equal("moka.devtools", new DevToolsModule().Info.Id);
}
