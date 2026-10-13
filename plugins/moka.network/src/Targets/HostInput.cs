using System.Diagnostics.CodeAnalysis;
using System.Net;
using Moka.Network.Lookup;

namespace Moka.Network.Targets;

/// <summary>
/// Reads the host box of ping, the port check and traceroute: a name, an address, an IPv6 address in brackets, or a
/// pasted URL, whose host is taken. A port belongs in its own field, so <c>host:22</c> is refused rather than guessed.
/// </summary>
public static class HostInput
{
	public static bool TryParse(string? text, [NotNullWhen(true)] out HostTarget? target, [NotNullWhen(false)] out string? problem)
	{
		target = null;
		string input = (text ?? "").Trim();
		if (input.Length == 0)
		{
			problem = "Type a host name or an address.";
			return false;
		}

		if (input.Contains("://", StringComparison.Ordinal))
		{
			if (!Uri.TryCreate(input, UriKind.Absolute, out Uri? uri) || uri.Host.Length == 0)
			{
				problem = "That looks like a link, but it has no host in it.";
				return false;
			}

			input = uri.Host;
		}

		if (input.StartsWith('[') && input.EndsWith(']'))
		{
			input = input[1..^1];
		}

		if (AddressText.TryParse(input, out IPAddress? address))
		{
			target = new HostTarget(address.ToString(), address);
			problem = null;
			return true;
		}

		if (input.Contains(':', StringComparison.Ordinal))
		{
			problem = $"{input} is neither a host name nor an address. Type the host alone; a port has its own field.";
			return false;
		}

		if (!DnsName.TryNormalize(input, out string? name, out problem))
		{
			return false;
		}

		if (name == DnsName.Root || name.Split('.').Contains("*"))
		{
			problem = $"{input} is not one machine's name.";
			return false;
		}

		if (LooksNumeric(name))
		{
			// The system resolver reads 10.1 or 0x7f.1 as an address in a short form, so it would probe a machine nobody named.
			problem = $"{input} is not a full address. Write IPv4 as four numbers, such as 10.0.0.1.";
			return false;
		}

		target = new HostTarget(name, null);
		return true;
	}

	/// <summary>True when the last label is a number (decimal, or hex after 0x): no top level domain is, but short IPv4 forms are.</summary>
	internal static bool LooksNumeric(string name)
	{
		string last = name[(name.LastIndexOf('.') + 1)..];
		if (last.Length > 0 && last.All(char.IsAsciiDigit))
		{
			return true;
		}

		return last.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && last[2..].All(char.IsAsciiHexDigit);
	}
}
