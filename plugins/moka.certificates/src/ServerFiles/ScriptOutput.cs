using System.Globalization;
using System.Text;

namespace Moka.Certificates.ServerFiles;

/// <summary>
/// Splits what the page's scripts printed. The discovery script prints <c>@@mc:NAME</c> before each part; the read script
/// prints <c>@@mc:file INDEX STATE</c> before each file. A line from the server that merely starts with the marker
/// cannot fake a part: parts are matched by their whole line, and files by a number the page chose.
/// </summary>
public static class ScriptOutput
{
	private const string NeedsRoot = ServerCertificateCommands.Marker + "needs-root";
	private const string Failed = ServerCertificateCommands.Marker + "failed";
	private const string FilePrefix = ServerCertificateCommands.Marker + "file ";

	public static DiscoveryResult ParseDiscovery(string output)
	{
		ArgumentNullException.ThrowIfNull(output);
		Dictionary<string, StringBuilder> parts = new(StringComparer.Ordinal);
		HashSet<string> flags = new(StringComparer.Ordinal);
		string? current = null;
		foreach (string line in output.ReplaceLineEndings("\n").Split('\n'))
		{
			if (line.StartsWith(ServerCertificateCommands.Marker, StringComparison.Ordinal)
				&& line[ServerCertificateCommands.Marker.Length..] is "certbot" or "live" or "nginx" or "apache" or "extra" or "end")
			{
				current = line[ServerCertificateCommands.Marker.Length..];
				parts[current] = new StringBuilder();
				continue;
			}

			if (current is null)
			{
				continue;
			}

			if (line is NeedsRoot or Failed)
			{
				flags.Add(current + ":" + line[ServerCertificateCommands.Marker.Length..]);
				continue;
			}

			parts[current].Append(line).Append('\n');
		}

		string Part(string name) => parts.TryGetValue(name, out StringBuilder? text) ? text.ToString() : "";
		(IReadOnlyList<ConfiguredCertificate> nginx, IReadOnlyList<string> skipped) = NginxConfig.Parse(Part("nginx"));
		return new DiscoveryResult
		{
			Lineages = CertbotOutput.Parse(Part("certbot")),
			CertbotNeedsRoot = flags.Contains("certbot:needs-root"),
			LiveFolders = Lines(Part("live")),
			LiveNeedsRoot = flags.Contains("live:needs-root"),
			Configured = [.. nginx, .. ApacheConfig.Parse(Part("apache"))],
			Skipped = skipped,
			NginxFailed = flags.Contains("nginx:failed"),
			ExtraFiles = Lines(Part("extra")),
		};
	}

	/// <summary>Each file's state and content, by the index the read script gave it.</summary>
	public static IReadOnlyDictionary<int, FileRead> ParseFiles(string output)
	{
		ArgumentNullException.ThrowIfNull(output);
		Dictionary<int, FileRead> files = [];
		int? index = null;
		string state = "";
		StringBuilder content = new();

		void Flush()
		{
			if (index is { } done)
			{
				files[done] = new FileRead(state, content.ToString());
			}

			content.Clear();
		}

		foreach (string line in output.ReplaceLineEndings("\n").Split('\n'))
		{
			if (line.StartsWith(FilePrefix, StringComparison.Ordinal))
			{
				string[] words = line[FilePrefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
				if (words.Length == 2 && int.TryParse(words[0], NumberStyles.None, CultureInfo.InvariantCulture, out int number))
				{
					Flush();
					index = number;
					state = words[1];
					continue;
				}
			}

			if (index is not null)
			{
				content.Append(line).Append('\n');
			}
		}

		Flush();
		return files;
	}

	private static List<string> Lines(string text) => [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
