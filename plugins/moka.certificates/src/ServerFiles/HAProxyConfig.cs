namespace Moka.Certificates.ServerFiles;

/// <summary>
/// Reads HAProxy's certificate settings from the discovery script's part: the <c>crt</c> and <c>crt-list</c> words of
/// <c>grep -Hn</c> lines from haproxy.cfg (<c>file:line:text</c>), then the contents of every crt-list file the
/// configuration names, each line prefixed with that file's own path (<c>listfile:entry</c>, so a line from a list
/// tells apart from a line from the configuration by the digits grep puts after the colon). A <c>crt</c> file and a
/// crt-list entry are both PEM bundles, counted as certificate files.
/// </summary>
public static class HAProxyConfig
{
	public static IReadOnlyList<ConfiguredCertificate> Parse(string partOutput)
	{
		ArgumentNullException.ThrowIfNull(partOutput);
		List<ConfiguredCertificate> found = [];
		foreach (string raw in partOutput.ReplaceLineEndings("\n").Split('\n'))
		{
			string line = raw.Trim();
			if (line.Length == 0)
			{
				continue;
			}

			int colon = line.IndexOf(':', StringComparison.Ordinal);
			if (colon <= 0 || colon + 1 >= line.Length)
			{
				continue;
			}

			string file = line[..colon];
			if (!file.StartsWith('/'))
			{
				continue;
			}

			string rest = line[(colon + 1)..];
			if (rest.Length > 0 && char.IsAsciiDigit(rest[0]))
			{
				// A configuration line: "12:bind *:443 ssl crt /etc/haproxy/site.pem crt-list /etc/haproxy/list".
				ParseBind(file, rest, found);
			}
			else
			{
				// A crt-list entry: the path is the first word, and HAProxy 2.3 allows options after it.
				string? entry = Words(rest).FirstOrDefault();
				if (entry?.StartsWith('/') == true)
				{
					found.Add(new ConfiguredCertificate(entry, "HAProxy", file, [], "crt-list"));
				}
			}
		}

		return found;
	}

	private static void ParseBind(string configFile, string line, List<ConfiguredCertificate> found)
	{
		int colon = line.IndexOf(':', StringComparison.Ordinal);
		string[] words = Words(colon >= 0 ? line[(colon + 1)..] : line);
		for (int index = 0; index < words.Length - 1; index++)
		{
			// crt names a PEM bundle; crt-list names a file of such bundles, whose contents arrive as their own lines.
			if (words[index] is "crt" && words[index + 1].StartsWith('/'))
			{
				found.Add(new ConfiguredCertificate(words[index + 1], "HAProxy", configFile, [], "crt"));
			}
		}
	}

	private static string[] Words(string text) =>
		[.. text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(word => word.Trim('"', '\''))];
}
