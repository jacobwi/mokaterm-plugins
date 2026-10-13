using System.Net;
using Moka.Network.Lookup;
using Moka.Network.Targets;

namespace Moka.Network.Whois;

/// <summary>
/// Finds where a whois answer sends the question next: IANA's <c>refer:</c> and <c>whois:</c>, a thin registry's
/// <c>Registrar WHOIS Server:</c> and ARIN's <c>ReferralServer: whois://...</c>. The value came from a server, so only a
/// plain public-looking host name on port 43 is followed: no addresses, no single-label names such as localhost, no
/// other scheme (rwhois is a different protocol) and no other port.
/// </summary>
public static class WhoisReferral
{
	private static readonly string[] Keys = ["refer", "whois", "ReferralServer", "Registrar WHOIS Server"];

	/// <summary>The first usable referral in <paramref name="answer"/>, as a lower case host name, or null.</summary>
	public static string? Find(string answer)
	{
		ArgumentNullException.ThrowIfNull(answer);
		foreach (string line in answer.Split('\n'))
		{
			int colon = line.IndexOf(':', StringComparison.Ordinal);
			if (colon <= 0 || colon > 40)
			{
				continue;
			}

			string key = line[..colon].Trim();
			if (!Keys.Any(known => string.Equals(known, key, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			if (ServerFrom(line[(colon + 1)..]) is { } server)
			{
				return server;
			}
		}

		return null;
	}

	/// <summary>The host a referral value names, or null when it is not one this client follows.</summary>
	public static string? ServerFrom(string value)
	{
		ArgumentNullException.ThrowIfNull(value);
		string text = value.Trim();
		if (text.Length is 0 or > 300)
		{
			return null;
		}

		const string Scheme = "whois://";
		if (text.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
		{
			text = text[Scheme.Length..];
		}
		else if (text.Contains("://", StringComparison.Ordinal))
		{
			return null;
		}

		text = text.TrimEnd('/');
		int colon = text.IndexOf(':', StringComparison.Ordinal);
		if (colon >= 0)
		{
			if (text[(colon + 1)..] != "43")
			{
				return null;
			}

			text = text[..colon];
		}

		if (IPAddress.TryParse(text, out _)
			|| !DnsName.TryNormalize(text, out string? name, out _)
			|| !name.Contains('.', StringComparison.Ordinal)
			|| name.Contains('*', StringComparison.Ordinal)
			|| name.Contains('_', StringComparison.Ordinal)
			|| name.EndsWith(".localhost", StringComparison.Ordinal)
			|| HostInput.LooksNumeric(name))
		{
			return null;
		}

		return name;
	}
}
