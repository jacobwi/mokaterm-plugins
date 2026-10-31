using System.Net;
using System.Net.Sockets;

namespace Moka.Network.Server;

/// <summary>
/// Reads the <c>nameserver</c> lines out of an <c>/etc/resolv.conf</c> the server sent: the resolvers the server
/// itself asks, which is the honest answer to "why does the server see that name differently". Anything else in the
/// file is left alone, and a nameserver that does not read as an address is skipped rather than shown, the same way
/// the resolver itself skips it.
/// </summary>
public static class ResolvConf
{
	public static IReadOnlyList<IPAddress> Nameservers(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		List<IPAddress> servers = [];
		foreach (string line in text.Split('\n'))
		{
			string trimmed = line.Trim();
			if (trimmed.StartsWith('#') || trimmed.StartsWith(';'))
			{
				continue;
			}

			string[] parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			if (parts.Length == 2
				&& parts[0] == "nameserver"
				&& IPAddress.TryParse(parts[1], out IPAddress? address)
				&& address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
			{
				servers.Add(address);
			}
		}

		return servers;
	}
}
