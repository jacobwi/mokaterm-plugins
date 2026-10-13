using Moka.Network.Lookup;
using Mokaterm.Abstractions.Settings;

namespace Moka.Network.Settings;

/// <summary>
/// The plugin's defaults, under <c>plugin.moka.network</c> in settings.json: which resolver the DNS tab starts with and
/// how long each kind of probe waits. Every range is a Min and Max pair here, read by <see cref="Clamped"/>, by the
/// settings page and by the tool, so none of them repeats it. Nothing the tool looks up is kept here.
/// </summary>
public sealed record NetworkSettings : ISettingsSection
{
	public const int MinDnsTimeoutSeconds = 1;

	public const int MaxDnsTimeoutSeconds = 30;

	public const int MinConnectTimeoutSeconds = 1;

	public const int MaxConnectTimeoutSeconds = 60;

	public const int MinWhoisTimeoutSeconds = 2;

	public const int MaxWhoisTimeoutSeconds = 60;

	public const int MinPingTimeoutMilliseconds = 100;

	public const int MaxPingTimeoutMilliseconds = 10000;

	public static string SectionKey => "plugin.moka.network";

	public DnsResolverKind Resolver { get; init; } = DnsResolverKind.System;

	/// <summary>The resolver for <see cref="DnsResolverKind.Custom"/>: an address with an optional port.</summary>
	public string CustomResolver { get; init; } = "";

	/// <summary>How long one DNS server gets to answer, over UDP and over TCP each.</summary>
	public int DnsTimeoutSeconds { get; init; } = 3;

	/// <summary>How long the port check waits for each connection.</summary>
	public int ConnectTimeoutSeconds { get; init; } = 5;

	/// <summary>How long each whois server gets, from connecting to its last byte.</summary>
	public int WhoisTimeoutSeconds { get; init; } = 10;

	/// <summary>How long ping and traceroute wait for each reply.</summary>
	public int PingTimeoutMilliseconds { get; init; } = 2000;

	public TimeSpan DnsTimeout => TimeSpan.FromSeconds(DnsTimeoutSeconds);

	public TimeSpan ConnectTimeout => TimeSpan.FromSeconds(ConnectTimeoutSeconds);

	public TimeSpan WhoisTimeout => TimeSpan.FromSeconds(WhoisTimeoutSeconds);

	/// <summary>A copy with every value inside what the settings page allows, since settings.json can be edited by hand.</summary>
	public NetworkSettings Clamped()
	{
		// Custom stays picked without an address, so the page can be set in either order; the DNS tab asks for one then.
		string custom = CustomResolver?.Trim() ?? "";
		return this with
		{
			Resolver = Enum.IsDefined(Resolver) ? Resolver : DnsResolverKind.System,
			CustomResolver = DnsServerAddress.Problem(custom) is null ? custom : "",
			DnsTimeoutSeconds = Math.Clamp(DnsTimeoutSeconds, MinDnsTimeoutSeconds, MaxDnsTimeoutSeconds),
			ConnectTimeoutSeconds = Math.Clamp(ConnectTimeoutSeconds, MinConnectTimeoutSeconds, MaxConnectTimeoutSeconds),
			WhoisTimeoutSeconds = Math.Clamp(WhoisTimeoutSeconds, MinWhoisTimeoutSeconds, MaxWhoisTimeoutSeconds),
			PingTimeoutMilliseconds = Math.Clamp(PingTimeoutMilliseconds, MinPingTimeoutMilliseconds, MaxPingTimeoutMilliseconds),
		};
	}
}
