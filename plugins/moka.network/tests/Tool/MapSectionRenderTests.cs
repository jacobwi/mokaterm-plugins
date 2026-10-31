using Microsoft.Extensions.DependencyInjection;
using Moka.Network.Targets;
using Moka.Network.Tool;
using Moka.Network.Whois;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Plugin.TestHost;

namespace Moka.Network.Tests.Tool;

/// <summary>The map section renders on both hosts before anything has run: its inputs, its sweep row, its button.</summary>
public sealed class MapSectionRenderTests
{
	[Theory]
	[InlineData(HostKind.Desktop)]
	[InlineData(HostKind.Web)]
	public async Task Map_IsInTheNavigationAndDrawsItsInputs(HostKind host)
	{
		string html = await RenderAsync(host);

		Assert.Contains("Map", html, StringComparison.Ordinal);
		Assert.Contains("Draw the map", html, StringComparison.Ordinal);
		Assert.Contains("PTR sweep of", html, StringComparison.Ordinal);
		Assert.Contains("Sweep PTRs", html, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(HostKind.Desktop, "Runs from this computer")]
	[InlineData(HostKind.Web, "Runs from the Mokaterm server")]
	public async Task Map_SaysWhereItsQuestionsAreAskedFrom(HostKind host, string origin)
	{
		string html = await RenderAsync(host);

		Assert.Contains(origin, html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(HostKind host) =>
		StaticRender.RenderAsync<NetworkTool>([], host, services =>
		{
			// What NetworkModule adds for the app run, so every section's injections resolve: the recent-targets memory
			// and the RDAP client with its transport.
			services.AddSingleton(new RecentTargets());
			services.AddSingleton<IRdapTransport, HttpsRdapTransport>();
			services.AddSingleton<RdapClient>();
		});
}
