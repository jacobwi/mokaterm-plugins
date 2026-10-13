using System.Globalization;

namespace Moka.Certificates.ServerFiles;

/// <summary>
/// Reads <c>certbot certificates</c>: a "Certificate Name:" line opens each lineage and the indented "Key: value" lines
/// under it describe it. Older certbot prints no serial and no key type; anything it prints that is not such a line
/// (the debug log notice, the dashed rules) is passed over.
/// </summary>
public static class CertbotOutput
{
	public static IReadOnlyList<CertbotCertificate> Parse(string output)
	{
		ArgumentNullException.ThrowIfNull(output);
		List<CertbotCertificate> lineages = [];
		CertbotCertificate? current = null;
		foreach (string raw in output.ReplaceLineEndings("\n").Split('\n'))
		{
			string line = raw.Trim();
			int colon = line.IndexOf(':', StringComparison.Ordinal);
			if (colon <= 0)
			{
				continue;
			}

			string key = line[..colon].Trim();
			string value = line[(colon + 1)..].Trim();
			if (key.Equals("Certificate Name", StringComparison.OrdinalIgnoreCase))
			{
				if (current is not null)
				{
					lineages.Add(current);
				}

				current = value.Length > 0 ? new CertbotCertificate { Name = value } : null;
				continue;
			}

			if (current is null)
			{
				continue;
			}

			current = key.ToUpperInvariant() switch
			{
				"SERIAL NUMBER" => current with { SerialNumber = value },
				"KEY TYPE" => current with { KeyType = value },
				"DOMAINS" => current with { Domains = value.Split(' ', StringSplitOptions.RemoveEmptyEntries) },
				"EXPIRY DATE" => WithExpiry(current, value),
				"CERTIFICATE PATH" => current with { CertificatePath = value },
				"PRIVATE KEY PATH" => current with { PrivateKeyPath = value },
				_ => current,
			};
		}

		if (current is not null)
		{
			lineages.Add(current);
		}

		return lineages;
	}

	// "2026-12-01 10:00:00+00:00 (VALID: 49 days)"
	private static CertbotCertificate WithExpiry(CertbotCertificate lineage, string value)
	{
		int bracket = value.IndexOf('(', StringComparison.Ordinal);
		string date = (bracket < 0 ? value : value[..bracket]).Trim();
		string? state = bracket < 0 ? null : value[(bracket + 1)..].TrimEnd(')', ' ');
		DateTimeOffset? expires = DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
			? parsed.ToUniversalTime()
			: null;
		return lineage with { Expires = expires, State = state };
	}
}
