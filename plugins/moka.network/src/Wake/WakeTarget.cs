using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using Moka.Network.Subnets;
using Moka.Network.Targets;

namespace Moka.Network.Wake;

/// <summary>
/// Where the magic packet goes: an IPv4 address (the limited broadcast 255.255.255.255 by default, or a subnet's own
/// broadcast such as 192.168.1.255), or a prefix such as 192.168.1.0/24, whose broadcast address is worked out.
/// </summary>
public static class WakeTarget
{
	public static readonly IPAddress LimitedBroadcast = IPAddress.Broadcast;

	public static bool TryParse(string? text, [NotNullWhen(true)] out IPAddress? address, [NotNullWhen(false)] out string? problem)
	{
		address = null;
		string input = (text ?? "").Trim();
		if (input.Length == 0)
		{
			address = LimitedBroadcast;
			problem = null;
			return true;
		}

		if (input.Contains('/', StringComparison.Ordinal))
		{
			if (!SubnetMath.TryCalculate(input, out SubnetInfo? subnet, out problem))
			{
				return false;
			}

			if (subnet.IsIPv6)
			{
				problem = "Wake-on-LAN goes to an IPv4 broadcast address.";
				return false;
			}

			if (subnet.Broadcast is not { } broadcast)
			{
				problem = $"A /{subnet.PrefixLength} has no broadcast address. Type the address to send to instead.";
				return false;
			}

			address = broadcast;
			return true;
		}

		if (!AddressText.TryParse(input, out IPAddress? parsed) || parsed.AddressFamily != AddressFamily.InterNetwork)
		{
			problem = $"{input} is not an IPv4 address. Use 255.255.255.255, a subnet's broadcast such as 192.168.1.255, or a prefix such as 192.168.1.0/24.";
			return false;
		}

		byte first = parsed.GetAddressBytes()[0];
		if (parsed.Equals(IPAddress.Any) || first is >= 224 and < 240)
		{
			problem = $"{parsed} cannot carry a magic packet: it is not a broadcast or a machine's address.";
			return false;
		}

		address = parsed;
		problem = null;
		return true;
	}
}
