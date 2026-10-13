using System.Globalization;
using System.Net;
using Moka.Certificates.Crypto;

namespace Moka.Certificates.Tls;

/// <summary>
/// Where a server check connects: a host name or address, a port, and the name sent as SNI (the host itself when it is
/// a name, none for an address unless one is given).
/// </summary>
public sealed record TlsTarget(string Host, int Port, string? ServerName)
{
	public const int DefaultPort = 443;

	/// <summary>The name the certificate is checked against: the SNI name, else the host.</summary>
	public string NameToCheck => ServerName ?? Host;

	/// <summary><c>host:port</c>, with an IPv6 address in brackets.</summary>
	public string Endpoint => IPAddress.TryParse(Host, out IPAddress? address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
		? string.Create(CultureInfo.InvariantCulture, $"[{Host}]:{Port}")
		: string.Create(CultureInfo.InvariantCulture, $"{Host}:{Port}");

	/// <summary>
	/// Reads what was typed. The host may be a name, an address, <c>host:port</c>, <c>[v6]:port</c> or a whole
	/// https address, whose port is taken when the port box is empty.
	/// </summary>
	public static bool TryParse(string? host, string? port, string? serverName, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out TlsTarget? target, [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? problem)
	{
		target = null;
		string text = (host ?? "").Trim();
		int? typedPort = null;
		if (text.Contains("://", StringComparison.Ordinal))
		{
			if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) || uri.Host.Length == 0)
			{
				problem = "That address could not be read.";
				return false;
			}

			text = uri.Host.Trim('[', ']');
			typedPort = uri.IsDefaultPort ? null : uri.Port;
		}
		else if (text.StartsWith('[') && text.IndexOf(']', StringComparison.Ordinal) is > 0 and int close)
		{
			string rest = text[(close + 1)..];
			text = text[1..close];
			if (rest.StartsWith(':') && int.TryParse(rest[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int bracketPort))
			{
				typedPort = bracketPort;
			}
		}
		else if (text.Count(c => c == ':') == 1)
		{
			int colon = text.IndexOf(':', StringComparison.Ordinal);
			if (int.TryParse(text[(colon + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out int hostPort))
			{
				typedPort = hostPort;
				text = text[..colon];
			}
		}

		if (text.Length == 0)
		{
			problem = "Type a host name or an address.";
			return false;
		}

		bool isAddress = IPAddress.TryParse(text, out IPAddress? address);
		string? hostName = isAddress ? address!.ToString() : SubjectNames.AsciiDomain(text, allowWildcard: false);
		if (hostName is null)
		{
			problem = $"{Shown(text)} is not a host name or an IP address.";
			return false;
		}

		int portNumber = DefaultPort;
		string portText = (port ?? "").Trim();
		if (portText.Length > 0)
		{
			if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out portNumber) || portNumber is < 1 or > 65535)
			{
				problem = "The port is a number from 1 to 65535.";
				return false;
			}
		}
		else if (typedPort is { } fromHost)
		{
			portNumber = fromHost;
		}

		if (portNumber is < 1 or > 65535)
		{
			problem = "The port is a number from 1 to 65535.";
			return false;
		}

		string? sni = null;
		string sniText = (serverName ?? "").Trim();
		if (sniText.Length > 0)
		{
			sni = IPAddress.TryParse(sniText, out _) ? null : SubjectNames.AsciiDomain(sniText, allowWildcard: false);
			if (sni is null)
			{
				problem = $"{Shown(sniText)} is not a host name. SNI carries a name, never an address.";
				return false;
			}
		}
		else if (!isAddress)
		{
			sni = hostName;
		}

		target = new TlsTarget(hostName, portNumber, sni);
		problem = null;
		return true;
	}

	private static string Shown(string value) => value.Length > 60 ? value[..60] + "..." : value;
}
