using System.Security.Cryptography;
using Moka.Certificates.Crypto;

namespace Moka.Certificates.ServerFiles;

/// <summary>
/// Turns what discovery found into the files to read, each with everything that uses it, and then turns what the read
/// script printed into the page's rows. Every file goes through <see cref="CertificateReader"/>, so a certificate here
/// is described exactly as the tool describes it; only certificates are kept from what was read.
/// </summary>
public static class ServerCertificateScan
{
	private const string LivePrefix = "/etc/letsencrypt/live/";

	/// <summary>The files to read, in a stable order (certbot's, then the web servers', then the extra paths), each once.</summary>
	public static IReadOnlyList<ScanCandidate> Candidates(DiscoveryResult discovery)
	{
		ArgumentNullException.ThrowIfNull(discovery);
		List<string> order = [];
		Dictionary<string, List<CertificateUse>> uses = new(StringComparer.Ordinal);

		void Add(string? path, CertificateUse use)
		{
			if (path is null || ServerCertificateCommands.PathProblem(path) is not null)
			{
				return;
			}

			if (!uses.TryGetValue(path, out List<CertificateUse>? list))
			{
				list = [];
				uses[path] = list;
				order.Add(path);
			}

			if (!list.Contains(use))
			{
				list.Add(use);
			}
		}

		foreach (CertbotCertificate lineage in discovery.Lineages)
		{
			Add(lineage.CertificatePath, new CertificateUse("certbot", lineage.Domains.Count > 0 ? $"{lineage.Name}: {string.Join(" ", lineage.Domains)}" : lineage.Name));
		}

		foreach (string folder in discovery.LiveFolders)
		{
			string path = folder.TrimEnd('/') + "/fullchain.pem";
			if (!uses.ContainsKey(path))
			{
				Add(path, new CertificateUse("certbot", LineageName(path) ?? folder));
			}
		}

		foreach (ConfiguredCertificate configured in discovery.Configured)
		{
			string names = configured.ServerNames.Count > 0 ? string.Join(" ", configured.ServerNames) : "every server without its own";
			Add(configured.Path, new CertificateUse(configured.Server, $"{configured.ConfigFile} ({configured.Directive}), {names}"));
		}

		foreach (string path in discovery.ExtraFiles)
		{
			Add(path, new CertificateUse("Settings", "An extra path from the Certificates settings"));
		}

		return [.. order.Take(ServerCertificateCommands.MaxFiles).Select(path => new ScanCandidate(path, uses[path], LineageFor(path, discovery.Lineages)))];
	}

	/// <summary>One row per candidate, from what the read script printed about it.</summary>
	public static IReadOnlyList<ServerCertificateFile> Build(
		IReadOnlyList<ScanCandidate> candidates,
		IReadOnlyDictionary<int, FileRead> reads,
		bool readAsRoot,
		DateTimeOffset now,
		int expiringDays)
	{
		ArgumentNullException.ThrowIfNull(candidates);
		ArgumentNullException.ThrowIfNull(reads);
		List<ServerCertificateFile> files = [];
		for (int index = 0; index < candidates.Count; index++)
		{
			ScanCandidate candidate = candidates[index];
			ServerCertificateFile row = new()
			{
				Path = candidate.Path,
				Status = ServerFileStatus.NotRead,
				Uses = candidate.Uses,
				Lineage = candidate.Lineage,
				Problem = "The read stopped before this file: the output reached its limit.",
			};

			if (reads.TryGetValue(index, out FileRead? read))
			{
				row = read.State switch
				{
					"missing" => row with { Status = ServerFileStatus.Missing, Problem = "The file does not exist." },
					"unreadable" => row with
					{
						Status = ServerFileStatus.Unreadable,
						Problem = readAsRoot ? "The file cannot be read, even as root." : "This login cannot read the file. Turn root mode on to read it.",
					},
					"folder" => row with { Status = ServerFileStatus.Folder, Problem = "This is a folder, not a file." },
					"pem" => Certificates(row, CryptoInput.FromText(read.Content), "The file holds no certificate block.", now, expiringDays),
					"der" => FromBase64(row, read.Content, now, expiringDays),
					_ => row with { Status = ServerFileStatus.NoCertificate, Problem = "The file is not PEM, and too large to be one certificate in DER." },
				};
			}

			files.Add(row);
		}

		return files;
	}

	/// <summary>The certbot lineage a path under /etc/letsencrypt/live belongs to, by its folder name.</summary>
	public static string? LineageName(string path)
	{
		ArgumentNullException.ThrowIfNull(path);
		if (!path.StartsWith(LivePrefix, StringComparison.Ordinal))
		{
			return null;
		}

		string rest = path[LivePrefix.Length..];
		int slash = rest.IndexOf('/', StringComparison.Ordinal);
		string name = slash < 0 ? rest : rest[..slash];
		return ServerCertificateCommands.LineageProblem(name) is null ? name : null;
	}

	private static CertbotCertificate? LineageFor(string path, IReadOnlyList<CertbotCertificate> lineages)
	{
		string? name = LineageName(path);
		CertbotCertificate? listed = lineages.FirstOrDefault(lineage =>
			string.Equals(lineage.CertificatePath, path, StringComparison.Ordinal) || (name is not null && lineage.Name == name));
		return listed ?? (name is null ? null : new CertbotCertificate { Name = name });
	}

	private static ServerCertificateFile FromBase64(ServerCertificateFile row, string content, DateTimeOffset now, int expiringDays)
	{
		if (!BinaryText.TryParseBase64(content, out byte[]? bytes))
		{
			return row with { Status = ServerFileStatus.NoCertificate, Problem = "The file is not a certificate." };
		}

		try
		{
			return Certificates(row, CryptoInput.FromFile(row.Path, bytes), "The file is not a certificate.", now, expiringDays);
		}
		finally
		{
			// A DER file named as a certificate could still be a key; its bytes do not outlive this.
			CryptographicOperations.ZeroMemory(bytes);
		}
	}

	private static ServerCertificateFile Certificates(ServerCertificateFile row, CryptoInput input, string empty, DateTimeOffset now, int expiringDays)
	{
		using MaterialSet set = CertificateReader.Read(input);
		IReadOnlyList<System.Security.Cryptography.X509Certificates.X509Certificate2> certificates = set.Certificates;
		if (certificates.Count == 0)
		{
			return row with { Status = ServerFileStatus.NoCertificate, Problem = empty };
		}

		return row with
		{
			Status = ServerFileStatus.Read,
			Problem = null,
			Certificates = [.. certificates.Select(certificate => CertificateReader.Describe(certificate, now, expiringDays))],
			Chain = certificates.Count > 1 ? ChainCheck.Analyze(certificates) : null,
		};
	}
}
