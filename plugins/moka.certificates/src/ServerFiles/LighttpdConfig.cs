using System.Text.RegularExpressions;

namespace Moka.Certificates.ServerFiles;

/// <summary>
/// Reads lighttpd's <c>ssl.pemfile</c> lines from <c>grep -Hn</c> output (<c>file:line:text</c>): one PEM file per
/// server, holding the certificate, its key and the chain together, so it counts as a certificate file. A relative
/// path is relative to a server root lighttpd does not name here, so it is said rather than guessed.
/// </summary>
public static partial class LighttpdConfig
{
	public static (IReadOnlyList<ConfiguredCertificate> Certificates, IReadOnlyList<string> Skipped) Parse(string grepOutput)
	{
		ArgumentNullException.ThrowIfNull(grepOutput);
		List<ConfiguredCertificate> found = [];
		List<string> skipped = [];
		foreach (string raw in grepOutput.ReplaceLineEndings("\n").Split('\n'))
		{
			Match match = LinePattern().Match(raw);
			if (!match.Success)
			{
				continue;
			}

			// grep hands over ssl.pemfile lines only; a line that does not name one is not trusted anyway.
			if (!match.Groups["text"].Value.Contains("ssl.pemfile", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			string file = match.Groups["file"].Value;
			string value = Value(match.Groups["text"].Value);
			if (value.StartsWith('/'))
			{
				found.Add(new ConfiguredCertificate(value, "lighttpd", file, [], "ssl.pemfile"));
			}
			else if (value.Length > 0)
			{
				skipped.Add($"{file}: ssl.pemfile {value} is relative, and lighttpd does not say where to, so it cannot be read from here.");
			}
		}

		return (found, skipped);
	}

	/// <summary>The path in <c>ssl.pemfile = "/etc/lighttpd/server.pem"</c>, quotes and all comments off.</summary>
	private static string Value(string text)
	{
		int equals = text.IndexOf('=', StringComparison.Ordinal);
		string rest = (equals >= 0 ? text[(equals + 1)..] : text).Split('#', 2)[0].Trim();
		if (rest.Length == 0)
		{
			return "";
		}

		// mod_setenv style values may list more than one; the file is the first.
		return rest.Split([' ', '\t'], 2)[0].Trim('"', '\'');
	}

	[GeneratedRegex(@"^(?<file>/.+?):(?<line>[0-9]+):(?<text>.*)$", RegexOptions.CultureInvariant)]
	private static partial Regex LinePattern();
}
