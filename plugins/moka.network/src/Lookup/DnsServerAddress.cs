using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Moka.Network.Targets;

namespace Moka.Network.Lookup;

/// <summary>
/// A resolver typed as <c>ip</c> or <c>ip:port</c>: <c>192.168.1.1</c>, <c>192.168.1.1:5353</c>, <c>2001:db8::53</c>,
/// <c>[2001:db8::53]:5353</c>. Addresses only, since looking up the resolver's own name would need a resolver.
/// </summary>
public static class DnsServerAddress
{
	public const int DefaultPort = 53;

	public static bool TryParse(string? text, [NotNullWhen(true)] out IPEndPoint? endpoint, [NotNullWhen(false)] out string? problem)
	{
		endpoint = null;
		string input = (text ?? "").Trim();
		if (input.Length == 0)
		{
			problem = "Type the resolver's address, such as 192.168.1.1 or 192.168.1.1:5353.";
			return false;
		}

		string host = input;
		string? port = null;
		if (input.StartsWith('['))
		{
			int close = input.IndexOf(']', StringComparison.Ordinal);
			if (close < 0)
			{
				problem = "An IPv6 address in brackets needs its closing bracket.";
				return false;
			}

			host = input[1..close];
			string rest = input[(close + 1)..];
			if (rest.Length > 0)
			{
				if (!rest.StartsWith(':'))
				{
					problem = "After the bracket comes a colon and the port.";
					return false;
				}

				port = rest[1..];
			}
		}
		else if (input.Count(c => c == ':') == 1)
		{
			int colon = input.IndexOf(':', StringComparison.Ordinal);
			host = input[..colon];
			port = input[(colon + 1)..];
		}

		if (!AddressText.TryParse(host, out IPAddress? address))
		{
			problem = $"{host} is not an IP address. The resolver is given by address, not by name.";
			return false;
		}

		int number = DefaultPort;
		if (port is not null && (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out number) || number is < 1 or > 65535))
		{
			problem = $"{port} is not a port. Ports run from 1 to 65535.";
			return false;
		}

		if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.Broadcast)
			|| (address.AddressFamily == AddressFamily.InterNetwork && address.GetAddressBytes()[0] >= 224))
		{
			problem = $"{address} cannot answer a query: it is not one machine's address.";
			return false;
		}

		endpoint = new IPEndPoint(address, number);
		problem = null;
		return true;
	}

	/// <summary>Why <paramref name="text"/> cannot be used as a resolver, or null; for the settings field.</summary>
	public static string? Problem(string text) => TryParse(text, out _, out string? problem) ? null : problem;
}
