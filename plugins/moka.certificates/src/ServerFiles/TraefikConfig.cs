using System.Text.RegularExpressions;

namespace Moka.Certificates.ServerFiles;

/// <summary>
/// Reads Traefik's <c>certFile</c> values from <c>grep -RIn</c> output (<c>file:line:text</c>) under /etc/traefik:
/// the dynamic configuration spells them in YAML (<c>certFile: /path</c>) or TOML (<c>certFile = "/path"</c>), and
/// only the minimal reading of that one word is attempted, since the rest of the file's shape does not matter here.
/// A relative path is read against the folder of the file that names it, as Traefik resolves them. <c>keyFile</c> is
/// not looked for: a key is not a certificate.
/// </summary>
public static partial class TraefikConfig
{
	public static IReadOnlyList<ConfiguredCertificate> Parse(string grepOutput)
	{
		ArgumentNullException.ThrowIfNull(grepOutput);
		List<ConfiguredCertificate> found = [];
		foreach (string raw in grepOutput.ReplaceLineEndings("\n").Split('\n'))
		{
			Match match = LinePattern().Match(raw);
			if (!match.Success)
			{
				continue;
			}

			// grep hands over certFile lines only; a line that does not name one is not trusted anyway.
			if (!match.Groups["text"].Value.Contains("certFile", StringComparison.Ordinal))
			{
				continue;
			}

			string file = match.Groups["file"].Value;
			string value = Value(match.Groups["text"].Value);
			if (value.Length == 0)
			{
				continue;
			}

			if (!value.StartsWith('/'))
			{
				// Traefik reads a relative path against the folder of the file that names it.
				value = file[..(file.LastIndexOf('/') + 1)] + value;
			}

			found.Add(new ConfiguredCertificate(value, "Traefik", file, [], "certFile"));
		}

		return found;
	}

	/// <summary>The path after the colon of YAML or the equals sign of TOML, quotes off, nothing after a comment.</summary>
	private static string Value(string text)
	{
		int separator = text.IndexOf(':', StringComparison.Ordinal);
		if (separator < 0)
		{
			separator = text.IndexOf('=', StringComparison.Ordinal);
		}

		if (separator < 0)
		{
			return "";
		}

		return text[(separator + 1)..].Split('#', 2)[0].Trim().Split([' ', '\t'], 2)[0].Trim('"', '\'');
	}

	[GeneratedRegex(@"^(?<file>/.+?):(?<line>[0-9]+):(?<text>.*)$", RegexOptions.CultureInvariant)]
	private static partial Regex LinePattern();
}
