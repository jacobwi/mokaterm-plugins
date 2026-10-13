using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Moka.Network.Lookup;

/// <summary>The servers behind each resolver choice, in the order they are tried.</summary>
public static class DnsResolvers
{
	/// <summary>At most this many servers are tried for one question, so a dead list cannot hold a lookup for long.</summary>
	public const int MaxServersTried = 3;

	public static string Label(DnsResolverKind kind) => kind switch
	{
		DnsResolverKind.Cloudflare => "Cloudflare 1.1.1.1",
		DnsResolverKind.Google => "Google 8.8.8.8",
		DnsResolverKind.Quad9 => "Quad9 9.9.9.9",
		DnsResolverKind.Custom => "Custom",
		_ => "This machine's DNS",
	};

	/// <summary>
	/// The servers for <paramref name="kind"/>, or an empty list with the reason. Each public service has a second
	/// address on another network, which is tried when the first does not answer.
	/// </summary>
	public static IReadOnlyList<IPEndPoint> Servers(DnsResolverKind kind, string? custom, out string? problem)
	{
		problem = null;
		switch (kind)
		{
			case DnsResolverKind.Cloudflare:
				return Pair("1.1.1.1", "1.0.0.1");
			case DnsResolverKind.Google:
				return Pair("8.8.8.8", "8.8.4.4");
			case DnsResolverKind.Quad9:
				return Pair("9.9.9.9", "149.112.112.112");
			case DnsResolverKind.Custom:
				return DnsServerAddress.TryParse(custom, out IPEndPoint? endpoint, out problem) ? [endpoint] : [];
			default:
				IReadOnlyList<IPAddress> system = SystemServers();
				if (system.Count == 0)
				{
					problem = "This machine's network settings name no DNS servers. Pick a public resolver instead.";
					return [];
				}

				return [.. system.Take(MaxServersTried).Select(address => new IPEndPoint(address, DnsServerAddress.DefaultPort))];
		}
	}

	/// <summary>
	/// The DNS servers of every network interface that is up, in the order the system lists them. Windows lists the
	/// deprecated site-local fec0:0:0:ffff::1 to 3 on an interface without IPv6 DNS, which nothing answers, so those are
	/// left out.
	/// </summary>
	public static IReadOnlyList<IPAddress> SystemServers()
	{
		List<IPAddress> servers = [];
		try
		{
			foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces())
			{
				if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
				{
					continue;
				}

				foreach (IPAddress address in network.GetIPProperties().DnsAddresses)
				{
					if (address.IsIPv6SiteLocal || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || servers.Contains(address))
					{
						continue;
					}

					servers.Add(address);
				}
			}
		}
		catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException or SocketException)
		{
			// A platform that will not list its interfaces has no system servers to offer; the public ones still work.
		}

		return servers;
	}

	private static IPEndPoint[] Pair(string first, string second) =>
		[new(IPAddress.Parse(first), DnsServerAddress.DefaultPort), new(IPAddress.Parse(second), DnsServerAddress.DefaultPort)];
}
