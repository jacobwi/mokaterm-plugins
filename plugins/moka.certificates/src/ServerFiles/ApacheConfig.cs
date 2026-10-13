using System.Text.RegularExpressions;

namespace Moka.Certificates.ServerFiles;

/// <summary>
/// Reads the certificate lines of Apache's configuration from <c>grep -RHn</c> output (<c>file:line:text</c>): the
/// <c>&lt;VirtualHost&gt;</c> blocks, their <c>ServerName</c> and <c>ServerAlias</c>, and each
/// <c>SSLCertificateFile</c> and <c>SSLCertificateChainFile</c> in them. Lines come one file after another, so a block
/// left open at the end of a file is closed there. Relative paths are relative to the ServerRoot the file sits under.
/// </summary>
public static partial class ApacheConfig
{
	public static IReadOnlyList<ConfiguredCertificate> Parse(string grepOutput)
	{
		ArgumentNullException.ThrowIfNull(grepOutput);
		List<ConfiguredCertificate> found = [];
		string? file = null;
		bool inHost = false;
		List<string> names = [];
		List<(string Path, string Directive)> certificates = [];

		void Close()
		{
			foreach ((string path, string directive) in certificates)
			{
				found.Add(new ConfiguredCertificate(path, "Apache", file ?? "", inHost ? [.. names] : [], directive));
			}

			inHost = false;
			names.Clear();
			certificates.Clear();
		}

		foreach (string raw in grepOutput.ReplaceLineEndings("\n").Split('\n'))
		{
			Match match = LinePattern().Match(raw);
			if (!match.Success)
			{
				continue;
			}

			string lineFile = match.Groups["file"].Value;
			if (lineFile != file)
			{
				Close();
				file = lineFile;
			}

			string[] words = Words(match.Groups["text"].Value);
			if (words.Length == 0)
			{
				continue;
			}

			string directive = words[0];
			if (directive.StartsWith("<VirtualHost", StringComparison.OrdinalIgnoreCase))
			{
				Close();
				inHost = true;
			}
			else if (directive.StartsWith("</VirtualHost", StringComparison.OrdinalIgnoreCase))
			{
				Close();
			}
			else if (directive.Equals("ServerName", StringComparison.OrdinalIgnoreCase) || directive.Equals("ServerAlias", StringComparison.OrdinalIgnoreCase))
			{
				// ServerName may carry a scheme and a port: https://www.example.com:443.
				names.AddRange(words.Skip(1).Select(HostOf).Where(name => name.Length > 0));
			}
			else if ((directive.Equals("SSLCertificateFile", StringComparison.OrdinalIgnoreCase) || directive.Equals("SSLCertificateChainFile", StringComparison.OrdinalIgnoreCase))
				&& words.Length > 1)
			{
				certificates.Add((Resolve(words[1], lineFile), directive.Equals("SSLCertificateFile", StringComparison.OrdinalIgnoreCase) ? "SSLCertificateFile" : "SSLCertificateChainFile"));
			}
		}

		Close();
		return found;
	}

	private static string[] Words(string text)
	{
		List<string> words = [];
		string rest = text.Trim();
		while (rest.Length > 0)
		{
			if (rest[0] == '"')
			{
				int end = rest.IndexOf('"', 1);
				words.Add(end < 0 ? rest[1..] : rest[1..end]);
				rest = end < 0 ? "" : rest[(end + 1)..].TrimStart();
				continue;
			}

			int space = rest.IndexOfAny([' ', '\t']);
			words.Add(space < 0 ? rest : rest[..space]);
			rest = space < 0 ? "" : rest[space..].TrimStart();
		}

		return [.. words.Select(word => word.TrimEnd('>'))];
	}

	private static string HostOf(string value)
	{
		string host = value;
		int scheme = host.IndexOf("://", StringComparison.Ordinal);
		if (scheme >= 0)
		{
			host = host[(scheme + 3)..];
		}

		int colon = host.LastIndexOf(':');
		return (colon > 0 && !host.Contains(']', StringComparison.Ordinal) ? host[..colon] : host).Trim('/');
	}

	private static string Resolve(string path, string configFile)
	{
		if (path.StartsWith('/'))
		{
			return path;
		}

		string root = configFile.StartsWith("/etc/httpd/", StringComparison.Ordinal) ? "/etc/httpd/" : "/etc/apache2/";
		return root + path;
	}

	[GeneratedRegex(@"^(?<file>/.+?):(?<line>[0-9]+):(?<text>.*)$", RegexOptions.CultureInvariant)]
	private static partial Regex LinePattern();
}
