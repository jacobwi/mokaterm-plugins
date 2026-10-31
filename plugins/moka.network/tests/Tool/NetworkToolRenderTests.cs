using Microsoft.Extensions.DependencyInjection;
using Moka.Network.Targets;
using Moka.Network.Tool;
using Moka.Network.Whois;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Plugin.TestHost;

namespace Moka.Network.Tests.Tool;

/// <summary>Every section renders without a failure on both hosts, before any probe has run.</summary>
public sealed class NetworkToolRenderTests
{
	[Theory]
	[InlineData(HostKind.Desktop)]
	[InlineData(HostKind.Web)]
	public async Task Tool_RendersEverySection(HostKind host)
	{
		string html = await RenderAsync(host);

		foreach (string title in new[] { "DNS lookup", "Ping", "Port check", "Traceroute", "Whois", "Wake-on-LAN", "Subnets" })
		{
			Assert.Contains(title, html, StringComparison.Ordinal);
		}

		Assert.Contains("mn-tool", html, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(HostKind.Desktop, "Runs from this computer")]
	[InlineData(HostKind.Web, "Runs from the Mokaterm server")]
	public async Task Tool_SaysOnBothHostsWhereEveryProbeComesFrom(HostKind host, string origin)
	{
		string html = await RenderAsync(host);

		Assert.Contains(origin, html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(HostKind host) =>
		StaticRender.RenderAsync<NetworkTool>([], host, services =>
		{
			// What NetworkModule adds for the app run, so the sections' injections resolve: the recent-targets memory
			// and the RDAP client with its transport.
			services.AddSingleton(new RecentTargets());
			services.AddSingleton<IRdapTransport, HttpsRdapTransport>();
			services.AddSingleton<RdapClient>();
		});
}
