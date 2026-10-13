using System.Diagnostics.CodeAnalysis;
using System.Net;
using Moka.Network.Lookup;
using Moka.Network.Targets;

namespace Moka.Network.Whois;

/// <summary>
/// The whois box: a domain or an IP address, or a pasted link whose host is taken. What is sent is exactly the
/// normalized form, so nothing typed can add a line or an option to the query.
/// </summary>
public static class WhoisInput
{
	public static bool TryParse(string? text, [NotNullWhen(true)] out string? query, [NotNullWhen(false)] out string? problem)
	{
		query = null;
		string input = (text ?? "").Trim();
		if (input.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(input, UriKind.Absolute, out Uri? uri) && uri.Host.Length > 0)
		{
			input = uri.Host;
		}

		if (input.StartsWith('[') && input.EndsWith(']'))
		{
			input = input[1..^1];
		}

		if (AddressText.TryParse(input, out IPAddress? address))
		{
			// A zone names a link on this machine; the registries know the address without it.
			query = new IPAddress(address.GetAddressBytes()).ToString();
			problem = null;
			return true;
		}

		if (input.Length == 0)
		{
			problem = "Type a domain, such as example.com, or an IP address.";
			return false;
		}

		if (!DnsName.TryNormalize(input, out string? name, out problem))
		{
			return false;
		}

		if (name == DnsName.Root || name.Contains('*', StringComparison.Ordinal) || HostInput.LooksNumeric(name))
		{
			problem = $"{input} is neither a domain nor a full IP address.";
			return false;
		}

		query = name;
		return true;
	}
}
