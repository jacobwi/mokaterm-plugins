using System.Net;
using Moka.Certificates.Crypto;

namespace Moka.Certificates.Tls;

/// <summary>
/// Whether a certificate is for a host name or address, the way browsers decide (RFC 6125): DNS names and IP addresses
/// from the subject alternative names, a wildcard standing for exactly one whole label at the left, and the common name
/// only when the certificate has no DNS names at all, which is what older clients still do.
/// </summary>
public static class HostNameMatch
{
	public static HostNameResult Check(CertificateInfo certificate, string host)
	{
		ArgumentNullException.ThrowIfNull(certificate);
		ArgumentNullException.ThrowIfNull(host);
		if (IPAddress.TryParse(host, out IPAddress? address))
		{
			bool matches = certificate.AlternativeNames.Any(name => name.Kind == GeneralNameKind.IpAddress
				&& IPAddress.TryParse(name.Value, out IPAddress? listed)
				&& listed.Equals(address));
			return matches ? HostNameResult.Matches : HostNameResult.NoMatch;
		}

		string? wanted = SubjectNames.AsciiDomain(host, allowWildcard: false);
		if (wanted is null)
		{
			return HostNameResult.NoMatch;
		}

		List<string> dnsNames = [.. certificate.AlternativeNames.Where(name => name.Kind == GeneralNameKind.Dns).Select(name => name.Value)];
		if (dnsNames.Any(pattern => Matches(pattern, wanted)))
		{
			return HostNameResult.Matches;
		}

		if (dnsNames.Count == 0
			&& certificate.SubjectParts.FirstOrDefault(part => part.Label == "CN") is { } commonName
			&& Matches(commonName.Value, wanted))
		{
			return HostNameResult.CommonNameOnly;
		}

		return HostNameResult.NoMatch;
	}

	/// <summary>Whether one certificate name covers <paramref name="host"/>, which is already in lower-case ASCII.</summary>
	public static bool Matches(string pattern, string host)
	{
		ArgumentNullException.ThrowIfNull(pattern);
		ArgumentNullException.ThrowIfNull(host);
		string name = pattern.TrimEnd('.');
		if (!name.StartsWith("*.", StringComparison.Ordinal))
		{
			return string.Equals(name, host, StringComparison.OrdinalIgnoreCase);
		}

		// The wildcard is the first label and only that: *.example.com covers www.example.com, not example.com and not
		// a.b.example.com. A wildcard over a single label (*.com) covers nothing.
		string suffix = name[1..];
		int firstDot = host.IndexOf('.', StringComparison.Ordinal);
		return firstDot > 0
			&& suffix.Count(c => c == '.') >= 2
			&& string.Equals(host[firstDot..], suffix, StringComparison.OrdinalIgnoreCase);
	}
}
