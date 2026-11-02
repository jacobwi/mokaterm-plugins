using Microsoft.Extensions.DependencyInjection;
using Moka.Network.Components;
using Moka.Network.Session;
using Moka.Network.Tool;
using Mokaterm.Abstractions.Sessions;
using Mokaterm.Plugin.TestHost;
using Mokaterm.UI.Common.Contributions;

namespace Moka.Network.Tests.Session;

/// <summary>The session tool with a fake session injected: what it shows before anything has run, connected and not.</summary>
public sealed class NetworkSessionToolRenderTests
{
	[Fact]
	public async Task Tool_RendersEverySectionOnAConnectedSession()
	{
		string html = await RenderAsync(FakeSessionHandle.FullyEquipped());

		foreach (string title in new[] { "Resolve", "Listening ports", "Reach", "Capture" })
		{
			Assert.Contains(title, html, StringComparison.Ordinal);
		}

		// The apostrophe in "session's" comes out HTML-encoded, so the assertion stops before it.
		Assert.Contains("Runs from the session", html, StringComparison.Ordinal);
		Assert.Contains("mn-session", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Tool_OnASessionThatCameApart_SaysThatRatherThanSomethingBroken()
	{
		FakeSessionHandle session = FakeSessionHandle.FullyEquipped();
		session.State = SessionState.Disconnected;

		string html = await RenderAsync(session);

		Assert.Contains("Resolve", html, StringComparison.Ordinal);
		Assert.Contains("The session is not connected", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Tool_TheCaptureTabSaysWhatItsFilterIsMadeOf()
	{
		string html = await RenderAsync(FakeSessionHandle.FullyEquipped());

		Assert.Contains("Three fields, nothing more", html, StringComparison.Ordinal);
		Assert.Contains("TCP", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(FakeSessionHandle session) =>
		StaticRender.RenderAsync<NetworkSessionTool>(
			new Dictionary<string, object?>
			{
				["Session"] = session,
				["IsActive"] = true,
			});
}

/// <summary>The desktop panel draws on its own, honest note included.</summary>
public sealed class DesktopConnectionsPanelRenderTests
{
	[Fact]
	public async Task Panel_RendersItsTableAndItsHonestLimit()
	{
		string html = await StaticRender.RenderAsync<DesktopConnectionsPanel>([]);

		Assert.Contains("mn-conn", html, StringComparison.Ordinal);
		Assert.Contains("do not say which process owns a connection", html, StringComparison.Ordinal);
	}
}

/// <summary>What the module adds for the session side: one session tool, on SSH, while connected.</summary>
public sealed class NetworkSessionToolRegistrationTests
{
	[Fact]
	public void Module_RegistersTheSessionToolForSshWhileConnected()
	{
		ServiceCollection services = new();

		new NetworkModule().ConfigureServices(services);
		using ServiceProvider provider = services.BuildServiceProvider();

		SessionToolDescriptor tool = Assert.Single(provider.GetServices<SessionToolDescriptor>());
		Assert.Equal("ssh", tool.ProtocolId);
		Assert.Equal(NetworkModule.SessionToolId, tool.Id);
		Assert.Equal(typeof(NetworkSessionTool), tool.Component);
		Assert.False(tool.ShowWhileDisconnected);
		Assert.Equal(SessionToolSize.Wide, tool.Size);
		Assert.Equal(NetworkIcons.Network, tool.Icon);
	}
}
