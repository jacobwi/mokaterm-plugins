using System.Net;
using System.Net.Sockets;

namespace Moka.Network.Server;

/// <summary>One line of <c>getent hosts</c>: the address as the server's own resolver hands it over, and the names it goes by there.</summary>
/// <param name="Address">The address the server answered with.</param>
/// <param name="Name">The canonical name on the line, which for a reverse lookup is the name the address belongs to.</param>
/// <param name="Aliases">The further names on the line, which most systems do not print.</param>
public sealed record GetentHost(IPAddress Address, string Name, IReadOnlyList<string> Aliases)
{
	public bool IsIPv6 => Address.AddressFamily == AddressFamily.InterNetworkV6;
}

/// <summary>
/// Reads what <c>getent hosts</c> printed: one address and the names for it per line, which is the answer the
/// <c>hosts</c> database on the server gives, exactly the point of asking there rather than on the desktop. Lines
/// that do not read that way are skipped rather than argued with, since the tool only reports what it understands.
/// </summary>
public static class GetentHosts
{
	public static IReadOnlyList<GetentHost> Parse(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		List<GetentHost> hosts = [];
		foreach (string line in text.Split('\n'))
		{
			string trimmed = line.Trim();
			if (trimmed.Length == 0)
			{
				continue;
			}

			string[] parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
			if (parts.Length < 2 || !IPAddress.TryParse(parts[0], out IPAddress? address))
			{
				continue;
			}

			string[] names = [.. parts[1..]];
			hosts.Add(new GetentHost(address, names[0], names[1..]));
		}

		return hosts;
	}
}
