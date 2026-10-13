using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Moka.Network.Targets;

/// <summary>
/// An IP address the way people type one: four dotted numbers or an IPv6 address. <see cref="IPAddress.TryParse(string, out IPAddress)"/>
/// alone also reads <c>10.1</c> or <c>167772161</c> as IPv4, which turns a typo into a probe of some other machine.
/// </summary>
public static partial class AddressText
{
	/// <summary>Reads an address. An IPv6 zone (<c>fe80::1%eth0</c>) is kept, since a link-local address needs it to be reached.</summary>
	public static bool TryParse(string? text, [NotNullWhen(true)] out IPAddress? address)
	{
		address = null;
		string input = (text ?? "").Trim();
		if (input.Length is 0 or > 100 || !IPAddress.TryParse(input, out IPAddress? parsed))
		{
			return false;
		}

		bool shaped = parsed.AddressFamily switch
		{
			AddressFamily.InterNetwork => DottedPattern().IsMatch(input),
			AddressFamily.InterNetworkV6 => input.Contains(':', StringComparison.Ordinal),
			_ => false,
		};

		if (!shaped)
		{
			return false;
		}

		address = parsed;
		return true;
	}

	/// <summary>True for four dotted numbers of up to three digits each, the only IPv4 form taken.</summary>
	public static bool IsDotted(string text) => DottedPattern().IsMatch(text);

	[GeneratedRegex(@"^[0-9]{1,3}(\.[0-9]{1,3}){3}\z", RegexOptions.CultureInvariant)]
	private static partial Regex DottedPattern();
}
