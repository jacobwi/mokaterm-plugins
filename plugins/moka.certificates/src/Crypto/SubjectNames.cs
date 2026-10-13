using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace Moka.Certificates.Crypto;

/// <summary>
/// Reads the subject alternative names typed for a new certificate: one per line (commas and spaces also separate),
/// each a DNS name, an IP address, an email address or a URI, told apart by its shape or by an OpenSSL-style prefix
/// such as <c>DNS:</c>. International domain names are stored in their ASCII form, as certificates require.
/// </summary>
public static partial class SubjectNames
{
	public const int MaxNames = 100;

	private static readonly IdnMapping Idn = new() { AllowUnassigned = false, UseStd3AsciiRules = true };

	public static (IReadOnlyList<GeneralNameInfo> Names, IReadOnlyList<string> Problems) Parse(string? text)
	{
		List<GeneralNameInfo> names = [];
		List<string> problems = [];
		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
		foreach (string entry in (text ?? "").Split([' ', ',', '\t', '\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries))
		{
			if (TryParseOne(entry, out GeneralNameInfo? name, out string? problem))
			{
				if (seen.Add(name.ToString()))
				{
					names.Add(name);
				}
			}
			else
			{
				problems.Add(problem);
			}
		}

		if (names.Count > MaxNames)
		{
			problems.Add($"At most {MaxNames} names fit here.");
		}

		return (names, problems);
	}

	/// <summary>The names as a certificate extension; critical when the subject is empty, as RFC 5280 requires.</summary>
	public static X509Extension ToExtension(IReadOnlyList<GeneralNameInfo> names, bool critical)
	{
		ArgumentNullException.ThrowIfNull(names);
		SubjectAlternativeNameBuilder builder = new();
		foreach (GeneralNameInfo name in names)
		{
			switch (name.Kind)
			{
				case GeneralNameKind.Dns:
					builder.AddDnsName(name.Value);
					break;
				case GeneralNameKind.IpAddress:
					builder.AddIpAddress(IPAddress.Parse(name.Value));
					break;
				case GeneralNameKind.Email:
					builder.AddEmailAddress(name.Value);
					break;
				case GeneralNameKind.Uri:
					builder.AddUri(new Uri(name.Value, UriKind.Absolute));
					break;
				default:
					throw new CertificateException($"A {name.Tag} name cannot be put in a new certificate here.");
			}
		}

		return builder.Build(critical);
	}

	private static bool TryParseOne(string entry, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out GeneralNameInfo? name, [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? problem)
	{
		name = null;
		problem = null;
		string value = entry.Trim();
		GeneralNameKind? kind = null;
		int colon = value.IndexOf(':', StringComparison.Ordinal);
		if (colon > 0)
		{
			kind = value[..colon].ToUpperInvariant() switch
			{
				"DNS" => GeneralNameKind.Dns,
				"IP" => GeneralNameKind.IpAddress,
				"EMAIL" => GeneralNameKind.Email,
				"URI" => GeneralNameKind.Uri,
				_ => null,
			};

			if (kind is not null)
			{
				value = value[(colon + 1)..].Trim();
			}
		}

		kind ??= Guess(value);
		switch (kind)
		{
			case GeneralNameKind.IpAddress:
				if (IPAddress.TryParse(value, out IPAddress? address) && (address.AddressFamily == AddressFamily.InterNetworkV6 || DottedPattern().IsMatch(value)))
				{
					// A zone such as %eth0 names a link on this machine; a certificate holds the bare address.
					bool zoned = address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0;
					name = new GeneralNameInfo(GeneralNameKind.IpAddress, (zoned ? new IPAddress(address.GetAddressBytes()) : address).ToString());
					return true;
				}

				problem = $"{Shown(value)} is not an IP address.";
				return false;
			case GeneralNameKind.Uri:
				if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && value.All(char.IsAscii))
				{
					name = new GeneralNameInfo(GeneralNameKind.Uri, uri.OriginalString);
					return true;
				}

				problem = $"{Shown(value)} is not a full URI, such as https://example.com/.";
				return false;
			case GeneralNameKind.Email:
			{
				int at = value.IndexOf('@', StringComparison.Ordinal);
				string? domain = at > 0 && at == value.LastIndexOf('@') ? AsciiDomain(value[(at + 1)..], allowWildcard: false) : null;
				if (domain is not null && value[..at].All(c => char.IsAscii(c) && !char.IsControl(c)))
				{
					name = new GeneralNameInfo(GeneralNameKind.Email, value[..at] + "@" + domain);
					return true;
				}

				problem = $"{Shown(value)} is not an email address a certificate can hold (ASCII before the @).";
				return false;
			}

			default:
				if (AsciiDomain(value, allowWildcard: true) is { } dns)
				{
					name = new GeneralNameInfo(GeneralNameKind.Dns, dns);
					return true;
				}

				problem = $"{Shown(value)} is not a host name. Use letters, digits and dashes between dots, with *. only at the start.";
				return false;
		}
	}

	private static GeneralNameKind Guess(string value)
	{
		if (value.Contains("://", StringComparison.Ordinal))
		{
			return GeneralNameKind.Uri;
		}

		if (value.Contains('@', StringComparison.Ordinal))
		{
			return GeneralNameKind.Email;
		}

		if (value.Contains(':', StringComparison.Ordinal) || DottedPattern().IsMatch(value))
		{
			return GeneralNameKind.IpAddress;
		}

		return GeneralNameKind.Dns;
	}

	/// <summary>A host name in its ASCII form, or null when it is not one. A leading <c>*.</c> stays for wildcards.</summary>
	internal static string? AsciiDomain(string value, bool allowWildcard)
	{
		string host = value.TrimEnd('.');
		string prefix = "";
		if (allowWildcard && host.StartsWith("*.", StringComparison.Ordinal))
		{
			prefix = "*.";
			host = host[2..];
		}

		if (host.Length == 0 || host.Length > 253)
		{
			return null;
		}

		string ascii;
		try
		{
			ascii = host.All(char.IsAscii) ? host : Idn.GetAscii(host);
		}
		catch (ArgumentException)
		{
			return null;
		}

		ascii = Lower(ascii);
		return ascii.Split('.').All(label => LabelPattern().IsMatch(label)) ? prefix + ascii : null;
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1308:Normalize strings to uppercase", Justification = "Host names are written in lower case, and this text is ASCII by now.")]
	private static string Lower(string ascii) => ascii.ToLowerInvariant();

	private static string Shown(string value) => value.Length > 60 ? value[..60] + "..." : value;

	[GeneratedRegex(@"^[0-9]{1,3}(\.[0-9]{1,3}){3}\z", RegexOptions.CultureInvariant)]
	private static partial Regex DottedPattern();

	[GeneratedRegex(@"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\z", RegexOptions.CultureInvariant)]
	private static partial Regex LabelPattern();
}
