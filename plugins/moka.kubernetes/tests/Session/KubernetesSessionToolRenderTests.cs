using Microsoft.Extensions.DependencyInjection;
using Moka.Kubernetes.Components;
using Moka.Kubernetes.Session;
using Moka.Kubernetes.Settings;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Plugin.TestHost;
using Mokaterm.UI.Common.Contributions;

namespace Moka.Kubernetes.Tests.Session;

/// <summary>The session tool with a fake session injected: what it shows before anything has run, connected and not.</summary>
public sealed class KubernetesSessionToolRenderTests
{
	[Theory]
	[InlineData(HostKind.Desktop)]
	[InlineData(HostKind.Web)]
	public async Task Tool_RendersEverySectionOnBothHosts(HostKind host)
	{
		string html = await RenderAsync(FakeSessionHandle.FullyEquipped(), host);

		foreach (string title in new[] { "Overview", "Pods", "Logs", "Command", "Rollouts" })
		{
			Assert.Contains(title, html, StringComparison.Ordinal);
		}

		Assert.Contains("mk8s-session", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Tool_OnAConnectedSession_SaysWhereEveryReadComesFrom()
	{
		string html = await RenderAsync(FakeSessionHandle.FullyEquipped());

		Assert.Contains("Reads through the session", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Tool_OnASessionThatCameApart_SaysThatRatherThanSomethingBroken()
	{
		FakeSessionHandle session = FakeSessionHandle.FullyEquipped();
		session.State = SessionState.Disconnected;

		string html = await RenderAsync(session);

		Assert.Contains("Overview", html, StringComparison.Ordinal);
		Assert.Contains("The session is not connected", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Tool_SaysTheNamespaceStoryOnThePodsTab()
	{
		string html = await RenderAsync(FakeSessionHandle.FullyEquipped());

		Assert.Contains("All namespaces", html, StringComparison.Ordinal);
		Assert.Contains("Reading only the namespace kubectl defaults to", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Tool_TheCommandTabStatesItsOwnHonestLimit()
	{
		string html = await RenderAsync(FakeSessionHandle.FullyEquipped());

		Assert.Contains("This is not a terminal", html, StringComparison.Ordinal);
		Assert.Contains("Run a command", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Tool_TheLogsTabSaysWhatFollowIs()
	{
		string html = await RenderAsync(FakeSessionHandle.FullyEquipped());

		Assert.Contains("a poll, not a stream", html, StringComparison.Ordinal);
		Assert.Contains("Previous reads what the container wrote before its latest start", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Tool_TheRolloutsTabNamesItsChangesAndTheirConfirmation()
	{
		string html = await RenderAsync(FakeSessionHandle.FullyEquipped());

		Assert.Contains("Restart and Undo change the cluster", html, StringComparison.Ordinal);
		Assert.Contains("with the exact kubectl command it is about to run", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Tool_TheOverviewTabCallsItselfARead()
	{
		string html = await RenderAsync(FakeSessionHandle.FullyEquipped());

		Assert.Contains("Nothing on this tab changes anything", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task SettingsPage_RendersItsThreeRows()
	{
		string html = await StaticRender.RenderAsync<KubernetesSettingsPage>([]);

		Assert.Contains("kubectl path", html, StringComparison.Ordinal);
		Assert.Contains("Command time limit", html, StringComparison.Ordinal);
		Assert.Contains("Namespace", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(FakeSessionHandle session, HostKind host = HostKind.Desktop) =>
		StaticRender.RenderAsync<KubernetesSessionTool>(
			new Dictionary<string, object?>
			{
				["Session"] = session,
				["IsActive"] = true,
			},
			host);
}

/// <summary>What the module adds: one session tool on SSH while connected, one settings page, and no app tool.</summary>
public sealed class KubernetesSessionToolRegistrationTests
{
	[Fact]
	public void Module_RegistersTheSessionToolForSshWhileConnected()
	{
		ServiceCollection services = new();

		new KubernetesModule().ConfigureServices(services);
		using ServiceProvider provider = services.BuildServiceProvider();

		SessionToolDescriptor tool = Assert.Single(provider.GetServices<SessionToolDescriptor>());
		Assert.Equal("ssh", tool.ProtocolId);
		Assert.Equal(KubernetesModule.SessionToolId, tool.Id);
		Assert.Equal("kubernetes", tool.Id);
		Assert.Equal(typeof(KubernetesSessionTool), tool.Component);
		Assert.False(tool.ShowWhileDisconnected);
		Assert.Equal(SessionToolSize.Wide, tool.Size);
		Assert.Equal(910, tool.Order);
		Assert.Equal(KubernetesIcons.Kubernetes, tool.Icon);
	}

	[Fact]
	public void Module_RegistersNoAppTool_BecauseKubectlLivesOnServers()
	{
		ServiceCollection services = new();

		new KubernetesModule().ConfigureServices(services);
		using ServiceProvider provider = services.BuildServiceProvider();

		Assert.Empty(provider.GetServices<AppToolDescriptor>());
	}

	[Fact]
	public void Module_RegistersTheSettingsPageUnderItsOwnKey()
	{
		ServiceCollection services = new();

		new KubernetesModule().ConfigureServices(services);
		using ServiceProvider provider = services.BuildServiceProvider();

		SettingsPageDescriptor settings = Assert.Single(provider.GetServices<SettingsPageDescriptor>());
		Assert.Equal(KubernetesSettings.SectionKey, settings.Id);
		Assert.Equal(SettingsPageGroup.Plugins, settings.Group);
		Assert.Equal(typeof(KubernetesSettingsPage), settings.Component);
		Assert.NotNull(settings.Keywords);
	}
}
