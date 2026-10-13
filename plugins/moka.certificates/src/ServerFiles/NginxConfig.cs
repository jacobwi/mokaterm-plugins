namespace Moka.Certificates.ServerFiles;

/// <summary>
/// Reads the certificates out of <c>nginx -T</c>, which prints every configuration file in turn after a
/// <c># configuration file PATH:</c> line. Each file is tokenized the way nginx reads it (words, quotes, comments,
/// <c>;</c>, <c>{</c> and <c>}</c>), and each <c>ssl_certificate</c> is recorded with the <c>server_name</c>s of the
/// server block around it. An included file is printed on its own, outside the block that included it, so a
/// <c>server</c> block at the top of any file counts as a server.
/// </summary>
public static class NginxConfig
{
	private const string FileMarker = "# configuration file ";

	/// <summary>The certificates named, and the values that could not be followed (variables, inline data).</summary>
	public static (IReadOnlyList<ConfiguredCertificate> Certificates, IReadOnlyList<string> Skipped) Parse(string output)
	{
		ArgumentNullException.ThrowIfNull(output);
		NginxReader reader = new();
		foreach (string line in output.ReplaceLineEndings("\n").Split('\n'))
		{
			if (line.StartsWith(FileMarker, StringComparison.Ordinal) && line.TrimEnd().EndsWith(':'))
			{
				reader.StartFile(line[FileMarker.Length..].TrimEnd()[..^1]);
				continue;
			}

			reader.Read(line);
		}

		return reader.Finish();
	}
}
