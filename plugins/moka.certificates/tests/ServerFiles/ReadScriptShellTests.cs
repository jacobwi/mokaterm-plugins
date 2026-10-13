using System.Diagnostics;
using System.Text;
using Moka.Certificates.Crypto;
using Moka.Certificates.ServerFiles;

namespace Moka.Certificates.Tests.ServerFiles;

/// <summary>
/// Runs the read script in a real POSIX shell against files on this machine, because the promise that matters (a key
/// next to a certificate never leaves the server) lives in the script, not in C#. Uses Git for Windows' sh, or /bin/sh
/// elsewhere; skipped where there is neither.
/// </summary>
public sealed class ReadScriptShellTests : IDisposable
{
	private readonly string _folder = Directory.CreateTempSubdirectory("mc-read-").FullName;

	public void Dispose() => Directory.Delete(_folder, recursive: true);

	[Theory]
	[InlineData("awk")]
	[InlineData("sed")]
	public async Task ReadScript_SendsCertificatesAndNeverKeys(string filter)
	{
		string? shell = FindShell();
		Assert.SkipWhen(shell is null, "No POSIX shell on this machine.");

		using TestChain chain = TestMaterial.Chain();
		string key = CertificateConverter.PrivateKeyPem(chain.LeafKey, PrivateKeyFormat.Pkcs8);
		string combined = key + TestMaterial.Pem(chain.Leaf) + CertificateConverter.PrivateKeyPem(chain.RootKey, PrivateKeyFormat.Traditional) + TestMaterial.Pem(chain.Intermediate);
		await File.WriteAllTextAsync(Path.Combine(_folder, "combined.pem"), combined.Replace("\n", "\r\n", StringComparison.Ordinal), TestContext.Current.CancellationToken);
		await File.WriteAllBytesAsync(Path.Combine(_folder, "root.der"), chain.Root.RawData, TestContext.Current.CancellationToken);
		await File.WriteAllTextAsync(Path.Combine(_folder, "key.pem"), key, TestContext.Current.CancellationToken);
		string[] paths = [ShellPath("combined.pem"), ShellPath("root.der"), ShellPath("key.pem"), ShellPath("missing.pem"), ShellPath("")];
		string[] commands = filter == "awk" ? ["awk", "base64"] : ["base64"];

		string output = await RunAsync(shell!, ServerCertificateCommands.ReadFiles(ServerCertificateCommandsTests.Server(commands), paths, asRoot: false).Body);

		Assert.DoesNotContain("PRIVATE KEY", output, StringComparison.Ordinal);
		Assert.DoesNotContain(KeyBody(key), output, StringComparison.Ordinal);
		IReadOnlyDictionary<int, FileRead> reads = ScriptOutput.ParseFiles(output);
		IReadOnlyList<ServerCertificateFile> files = ServerCertificateScan.Build(
			[.. paths.Select(path => new ScanCandidate(path, [], null))],
			reads,
			readAsRoot: false,
			TestMaterial.Now,
			30);
		Assert.Equal([ServerFileStatus.Read, ServerFileStatus.Read, ServerFileStatus.NoCertificate, ServerFileStatus.Missing, ServerFileStatus.Folder], files.Select(file => file.Status));
		Assert.Equal(["www.example.com", "Moka Test Intermediate"], files[0].Certificates.Select(certificate => certificate.Title));
		Assert.Equal("Moka Test Root", files[1].Leaf?.Title);
	}

	[Fact]
	public async Task DiscoverScript_RunsAndListsWhatAnExtraFolderHolds()
	{
		string? shell = FindShell();
		Assert.SkipWhen(shell is null, "No POSIX shell on this machine.");

		Directory.CreateDirectory(Path.Combine(_folder, "my certs"));
		foreach (string name in new[] { "a.pem", "b.crt", "c.cer", "notes.txt" })
		{
			await File.WriteAllTextAsync(Path.Combine(_folder, "my certs", name), "x", TestContext.Current.CancellationToken);
		}

		await File.WriteAllTextAsync(Path.Combine(_folder, "single.pem"), "x", TestContext.Current.CancellationToken);
		string folder = ShellPath("my certs");
		string single = ShellPath("single.pem");

		string output = await RunAsync(shell!, ServerCertificateCommands.Discover(ServerCertificateCommandsTests.Server(), [folder, single, ShellPath("absent.pem")], asRoot: false).Body);
		DiscoveryResult result = ScriptOutput.ParseDiscovery(output);

		Assert.Equal([$"{folder}/a.pem", $"{folder}/b.crt", $"{folder}/c.cer", single, ShellPath("absent.pem")], result.ExtraFiles);
		Assert.Empty(result.Configured);
		Assert.Empty(result.Lineages);
	}

	private static string KeyBody(string pem) => pem.Split('\n')[2];

	private string ShellPath(string name)
	{
		string full = Path.Combine(_folder, name).TrimEnd('\\', '/');
		if (!OperatingSystem.IsWindows())
		{
			return full;
		}

		// Git for Windows' sh reads C:\x as /c/x.
		return "/" + char.ToLowerInvariant(full[0]) + full[2..].Replace('\\', '/');
	}

	private static string? FindShell()
	{
		if (!OperatingSystem.IsWindows())
		{
			return File.Exists("/bin/sh") ? "/bin/sh" : null;
		}

		foreach (string? root in new[] { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramW6432") })
		{
			if (root is not null && Path.Combine(root, "Git", "bin", "sh.exe") is { } candidate && File.Exists(candidate))
			{
				return candidate;
			}
		}

		return null;
	}

	private static async Task<string> RunAsync(string shell, string script)
	{
		ProcessStartInfo start = new(shell, "-s")
		{
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			StandardOutputEncoding = Encoding.UTF8,
		};
		start.Environment["LC_ALL"] = "C";

		using Process process = Process.Start(start)!;
		using CancellationTokenSource limit = new(TimeSpan.FromSeconds(30));
		await process.StandardInput.WriteAsync(script.ReplaceLineEndings("\n"));
		process.StandardInput.Close();
		Task<string> output = process.StandardOutput.ReadToEndAsync(limit.Token);
		Task<string> error = process.StandardError.ReadToEndAsync(limit.Token);
		await process.WaitForExitAsync(limit.Token);
		Assert.True(process.ExitCode == 0, await error);
		return await output;
	}
}
