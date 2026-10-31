using System.Text.RegularExpressions;

namespace Moka.Certificates.ServerFiles;

/// <summary>
/// Reads the <c>tls</c> lines of a Caddyfile from <c>grep -Hn</c> output (<c>file:line:text</c>). A site block's
/// <c>tls CERT KEY</c> names a certificate file and its key; <c>tls internal</c> and <c>tls email</c> name nothing on
/// disk, and a value built from a placeholder cannot be followed. Only the main Caddyfile is looked at: its imports
/// are out of scope.
/// </summary>
public static partial class CaddyConfig
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

			// The first word is the directive; the certificate path, when there is one, follows it.
			string[] words = Words(match.Groups["text"].Value);
			if (words.Length < 2 || words[0] is not "tls")
			{
				continue;
			}

			string file = match.Groups["file"].Value;
			string value = words[1];
			if (value.StartsWith('/'))
			{
				// The key after the certificate is not a certificate, so it is not read.
				found.Add(new ConfiguredCertificate(value, "Caddy", file, [], "tls"));
			}
			else if (value.Contains('{') || value.Contains('$'))
			{
				skipped.Add($"{file}: tls {value} is chosen at runtime, so it cannot be read from here.");
			}

			// Anything else is an email address or the word internal: Caddy manages that certificate itself.
		}

		return (found, skipped);
	}

	private static string[] Words(string text) =>
		[.. text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(word => word.Trim('"', '\''))];

	[GeneratedRegex(@"^(?<file>/.+?):(?<line>[0-9]+):(?<text>.*)$", RegexOptions.CultureInvariant)]
	private static partial Regex LinePattern();
}
