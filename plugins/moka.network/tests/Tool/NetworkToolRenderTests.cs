using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Moka.Network.Components;
using Moka.Network.Echo;
using Moka.Network.Lookup;
using Moka.Network.Targets;
using Moka.Network.Tool;
using Moka.Network.Whois;
using Moka.Network.Tests.Lookup;
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

	[Fact]
	public async Task Tool_ShowsThePingModesTheMtuActionCustomPortsAndTheRace()
	{
		string html = await RenderAsync(HostKind.Desktop);

		Assert.Contains("ICMP", html, StringComparison.Ordinal);
		Assert.Contains("TCP", html, StringComparison.Ordinal);
		Assert.Contains("Find path MTU", html, StringComparison.Ordinal);
		Assert.Contains("Custom ports", html, StringComparison.Ordinal);
		Assert.Contains(">Race<", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task PingShapeField_ShowsTheSizeForIcmpAndThePortForTcp()
	{
		string icmp = await StaticRender.RenderAsync<PingShapeField>(Parameters(PingMode.Icmp), HostKind.Desktop);
		string tcp = await StaticRender.RenderAsync<PingShapeField>(Parameters(PingMode.Tcp), HostKind.Desktop);

		Assert.Contains("Size (bytes)", icmp, StringComparison.Ordinal);
		Assert.DoesNotContain("Size (bytes)", tcp, StringComparison.Ordinal);
		Assert.Contains("Port", tcp, StringComparison.Ordinal);
		Assert.DoesNotContain("Port", icmp, StringComparison.Ordinal);
		Assert.Contains("8443", tcp, StringComparison.Ordinal);

		static Dictionary<string, object?> Parameters(PingMode mode) => new()
		{
			[nameof(PingShapeField.Mode)] = mode,
			[nameof(PingShapeField.Size)] = "32",
			[nameof(PingShapeField.Port)] = "8443",
		};
	}

	[Fact]
	public async Task ResolverRaceTable_ShowsTheAnswersTheTimesAndTheFailures()
	{
		IReadOnlyList<ResolverRaceResult> results =
		[
			new(new ResolverRaceEntry("Cloudflare 1.1.1.1", [new(IPAddress.Parse("1.1.1.1"), 53)]), RaceReplies.Address("192.0.2.10"), null, TimeSpan.FromMilliseconds(12)),
			new(new ResolverRaceEntry("Google 8.8.8.8", [new(IPAddress.Parse("8.8.8.8"), 53)]), RaceReplies.Code(3), null, TimeSpan.FromMilliseconds(34)),
			new(new ResolverRaceEntry("This machine's DNS", []), null, "There is no DNS server to ask.", TimeSpan.FromMilliseconds(1)),
		];

		string html = await StaticRender.RenderAsync<ResolverRaceTable>(
			new Dictionary<string, object?> { [nameof(ResolverRaceTable.Results)] = results }, HostKind.Desktop);

		Assert.Contains("Cloudflare 1.1.1.1", html, StringComparison.Ordinal);
		Assert.Contains("192.0.2.10", html, StringComparison.Ordinal);
		Assert.Contains("NXDOMAIN", html, StringComparison.Ordinal);
		Assert.Contains("There is no DNS server to ask.", html, StringComparison.Ordinal);
		Assert.Contains("12 ms", html, StringComparison.Ordinal);
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
