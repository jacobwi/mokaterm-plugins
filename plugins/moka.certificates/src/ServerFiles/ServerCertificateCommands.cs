using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Abstractions.ServerAdmin;

namespace Moka.Certificates.ServerFiles;

/// <summary>
/// The scripts the Certificates page runs and the changes it offers. Paths and lineage names pass
/// <see cref="PathProblem"/> or <see cref="LineageProblem"/> and are then quoted; nothing from the server or from
/// settings reaches a script any other way. Certificate files are read through a filter that lets only certificate
/// blocks out, so a file that also holds a private key never sends the key over the connection.
/// </summary>
public static partial class ServerCertificateCommands
{
	public const string Certbot = "certbot";
	public const string Nginx = "nginx";
	public const string Apache2Ctl = "apache2ctl";
	public const string ApacheCtl = "apachectl";
	public const string Systemctl = "systemctl";
	public const string Awk = "awk";
	public const string Base64 = "base64";

	/// <summary>The marker every part and file of the scripts' output starts with.</summary>
	public const string Marker = "@@mc:";

	public const int MaxFiles = 200;

	/// <summary>The most of one file read; a certificate bundle is a few kilobytes.</summary>
	public const int MaxFileBytes = 1024 * 1024;

	/// <summary>A DER file larger than this is no single certificate, so it is not sent at all.</summary>
	public const int MaxDerBytes = 64 * 1024;

	public const int MaxPathLength = 1024;

	/// <summary>The folders whose Apache configuration is searched, Debian's and Red Hat's.</summary>
	public static readonly IReadOnlyList<string> ApacheFolders =
	[
		"/etc/apache2/apache2.conf",
		"/etc/apache2/sites-enabled",
		"/etc/apache2/conf-enabled",
		"/etc/apache2/mods-enabled",
		"/etc/httpd/conf",
		"/etc/httpd/conf.d",
	];

	/// <summary>The programs the page asks about, which the System view's detection probes because the descriptor names them.</summary>
	public static IReadOnlyList<string> Probed { get; } = [Certbot, Nginx, Apache2Ctl, ApacheCtl, Systemctl, Awk, Base64];

	private const string LetsEncryptLive = "/etc/letsencrypt/live";

	// awk keeps a line only from a CERTIFICATE begin line to its end line, and any other begin line turns it off, so a
	// key block in the same file is never printed. sed is the fallback where there is no awk.
	private const string CertificateFilterAwk = "awk '/^-----BEGIN /{keep=($0 ~ /^-----BEGIN CERTIFICATE-----/)} keep{print} /^-----END /{keep=0}'";
	private const string CertificateFilterSed = "sed -n '/^-----BEGIN CERTIFICATE-----/,/^-----END CERTIFICATE-----/p'";

	/// <summary>
	/// One read that finds where certificates are configured: certbot's lineages (as root only, since certbot refuses
	/// anyone else), the live folders, <c>nginx -T</c>, Apache's certificate lines, and each extra path from settings
	/// (a folder lists its .pem, .crt, .cer and .cert files).
	/// </summary>
	/// <exception cref="ArgumentException">An extra path is not one <see cref="PathProblem"/> accepts.</exception>
	public static SystemScript Discover(ServerFacts server, IReadOnlyList<string> extraPaths, bool asRoot)
	{
		ArgumentNullException.ThrowIfNull(server);
		ArgumentNullException.ThrowIfNull(extraPaths);
		StringBuilder body = new();
		body.Append(Part("certbot"));
		if (server.HasCommand(Certbot))
		{
			body.Append("if [ \"$(id -u)\" = 0 ]; then certbot certificates 2>&1; else printf '%sneeds-root\\n' '").Append(Marker).Append("'; fi\n");
		}

		body.Append(Part("live"))
			.Append("if [ -d ").Append(LetsEncryptLive).Append(" ]; then\n")
			.Append("\tif [ -r ").Append(LetsEncryptLive).Append(" ] && [ -x ").Append(LetsEncryptLive).Append(" ]; then\n")
			.Append("\t\tfor d in ").Append(LetsEncryptLive).Append("/*/; do [ -d \"$d\" ] && printf '%s\\n' \"$d\"; done\n")
			.Append("\telse printf '%sneeds-root\\n' '").Append(Marker).Append("'; fi\n")
			.Append("fi\n");

		body.Append(Part("nginx"));
		if (server.HasCommand(Nginx))
		{
			body.Append("nginx -T 2>/dev/null || printf '%sfailed\\n' '").Append(Marker).Append("'\n");
		}

		body.Append(Part("apache"))
			.Append("grep -RsHn -i -E '^[[:space:]]*(</?VirtualHost|ServerName|ServerAlias|SSLCertificateFile|SSLCertificateChainFile)([[:space:]>]|$)'");
		foreach (string folder in ApacheFolders)
		{
			body.Append(' ').Append(PosixShell.Quote(folder));
		}

		body.Append(" 2>/dev/null\n");

		body.Append(Part("extra"));
		foreach (string path in extraPaths)
		{
			if (PathProblem(path) is { } problem)
			{
				throw new ArgumentException(problem, nameof(extraPaths));
			}

			string quoted = PosixShell.Quote(path);
			body.Append("if [ -d ").Append(quoted).Append(" ]; then for f in ")
				.Append(quoted).Append("/*.pem ").Append(quoted).Append("/*.crt ").Append(quoted).Append("/*.cer ").Append(quoted).Append("/*.cert")
				.Append("; do [ -f \"$f\" ] && printf '%s\\n' \"$f\"; done; else printf '%s\\n' ").Append(quoted).Append("; fi\n");
		}

		body.Append(Part("end")).Append("exit 0\n");
		return new SystemScript
		{
			Name = "certificates.discover",
			Body = body.ToString(),
			AsRoot = asRoot,
		};
	}

	/// <summary>
	/// Reads each file in <paramref name="paths"/>: <c>@@mc:file INDEX STATE</c>, then for a PEM file its certificate
	/// blocks only, and for a small binary file its base64.
	/// </summary>
	/// <exception cref="ArgumentException">A path is not one <see cref="PathProblem"/> accepts, or there are too many.</exception>
	public static SystemScript ReadFiles(ServerFacts server, IReadOnlyList<string> paths, bool asRoot)
	{
		ArgumentNullException.ThrowIfNull(server);
		ArgumentNullException.ThrowIfNull(paths);
		if (paths.Count > MaxFiles)
		{
			throw new ArgumentException($"At most {MaxFiles} files are read at once.", nameof(paths));
		}

		string filter = server.HasCommand(Awk) ? CertificateFilterAwk : CertificateFilterSed;
		string der = server.HasCommand(Base64)
			? string.Create(CultureInfo.InvariantCulture, $"if [ \"$(wc -c < \"$2\" | tr -d ' ')\" -le {MaxDerBytes} ]; then printf '{Marker}file %s der\\n' \"$1\"; base64 < \"$2\"; else printf '{Marker}file %s other\\n' \"$1\"; fi")
			: $"printf '{Marker}file %s other\\n' \"$1\"";
		StringBuilder body = new();
		body.Append("mc_file() {\n")
			.Append("\tif [ ! -e \"$2\" ]; then printf '").Append(Marker).Append("file %s missing\\n' \"$1\"; return; fi\n")
			.Append("\tif [ -d \"$2\" ]; then printf '").Append(Marker).Append("file %s folder\\n' \"$1\"; return; fi\n")
			.Append("\tif [ ! -r \"$2\" ]; then printf '").Append(Marker).Append("file %s unreadable\\n' \"$1\"; return; fi\n")
			.Append("\tif grep -q -e '-----BEGIN' \"$2\" 2>/dev/null; then\n")
			.Append("\t\tprintf '").Append(Marker).Append("file %s pem\\n' \"$1\"\n")
			.Append("\t\thead -c ").Append(MaxFileBytes.ToString(CultureInfo.InvariantCulture)).Append(" \"$2\" | ").Append(filter).Append('\n')
			.Append("\telse\n")
			.Append("\t\t").Append(der).Append('\n')
			.Append("\tfi\n")
			.Append("}\n");
		for (int index = 0; index < paths.Count; index++)
		{
			if (PathProblem(paths[index]) is { } problem)
			{
				throw new ArgumentException(problem, nameof(paths));
			}

			body.Append("mc_file ").Append(index.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(PosixShell.Quote(paths[index])).Append('\n');
		}

		body.Append("exit 0\n");
		return new SystemScript
		{
			Name = "certificates.read",
			Body = body.ToString(),
			AsRoot = asRoot,
		};
	}

	/// <summary>Asks certbot to renew against the staging server and change nothing: every lineage, or one.</summary>
	/// <exception cref="ArgumentException">The lineage name is not one <see cref="LineageProblem"/> accepts.</exception>
	public static SystemChange RenewDryRun(string? lineage)
	{
		string command = "certbot renew --dry-run";
		if (lineage is not null)
		{
			ThrowIfBad(LineageProblem(lineage), nameof(lineage));
			command += " --cert-name " + PosixShell.Quote(lineage);
		}

		return new SystemChange
		{
			Title = lineage is null ? "Test renewing every certificate" : $"Test renewing {lineage}",
			Description = "certbot runs the whole renewal against Let's Encrypt's staging server and saves nothing, so you see whether the real renewal would work.",
			ConfirmText = "Test",
			Script = new SystemScript
			{
				Name = "certificates.renew-dry-run",
				Body = command,
				AsRoot = true,
				Timeout = TimeSpan.FromMinutes(10),
			},
		};
	}

	/// <summary>Renews one lineage, which certbot does only when it is due.</summary>
	/// <exception cref="ArgumentException">The lineage name is not one <see cref="LineageProblem"/> accepts.</exception>
	public static SystemChange Renew(string lineage)
	{
		ThrowIfBad(LineageProblem(lineage), nameof(lineage));
		return new SystemChange
		{
			Title = $"Renew {lineage}",
			Description = "certbot renews the certificate if it is due (by default within 30 days of expiry) and runs its deploy hooks. A web server that has no reload hook keeps the old certificate until it is reloaded.",
			Warning = "Let's Encrypt limits how often the same certificate can be issued; test with a dry run first when in doubt.",
			ConfirmText = "Renew",
			Script = new SystemScript
			{
				Name = "certificates.renew",
				Body = "certbot renew --cert-name " + PosixShell.Quote(lineage),
				AsRoot = true,
				Timeout = TimeSpan.FromMinutes(10),
			},
		};
	}

	/// <summary>Tests nginx's configuration and reloads it only when the test passes.</summary>
	public static SystemChange ReloadNginx(ServerFacts server)
	{
		ArgumentNullException.ThrowIfNull(server);
		bool systemd = UsesSystemd(server);
		return new SystemChange
		{
			Title = "Reload nginx",
			Description = "nginx tests its configuration and, only if that passes, loads the certificates again without dropping connections.",
			Warning = "A configuration that fails the test is not reloaded: nginx keeps running with the one it has.",
			ConfirmText = "Reload",
			Script = new SystemScript
			{
				Name = "certificates.reload-nginx",
				Body = systemd ? "nginx -t && systemctl reload nginx" : "nginx -t && nginx -s reload",
				AsRoot = true,
			},
		};
	}

	/// <summary>Tests Apache's configuration and reloads it gracefully. Null when neither apache2ctl nor apachectl is there.</summary>
	public static SystemChange? ReloadApache(ServerFacts server)
	{
		ArgumentNullException.ThrowIfNull(server);
		string? control = server.HasCommand(Apache2Ctl) ? Apache2Ctl : server.HasCommand(ApacheCtl) ? ApacheCtl : null;
		if (control is null)
		{
			return null;
		}

		// Debian names the unit apache2 and ships apache2ctl; Red Hat names it httpd.
		string unit = control == Apache2Ctl ? "apache2" : "httpd";
		return new SystemChange
		{
			Title = "Reload Apache",
			Description = "Apache tests its configuration and, only if that passes, restarts its workers gracefully so they load the certificates again.",
			Warning = "A configuration that fails the test is not reloaded: Apache keeps running with the one it has.",
			ConfirmText = "Reload",
			Script = new SystemScript
			{
				Name = "certificates.reload-apache",
				Body = UsesSystemd(server) ? $"{control} configtest && systemctl reload {unit}" : $"{control} configtest && {control} graceful",
				AsRoot = true,
			},
		};
	}

	/// <summary>Why <paramref name="path"/> cannot be read from here, or null: an absolute path without control characters.</summary>
	public static string? PathProblem(string? path)
	{
		if (string.IsNullOrEmpty(path) || path[0] != '/')
		{
			return $"\"{Shown(path)}\" is not an absolute path: it starts with /.";
		}

		if (path.Length > MaxPathLength)
		{
			return $"A path is at most {MaxPathLength} characters.";
		}

		return path.Any(char.IsControl) ? "A path cannot hold control characters or line breaks." : null;
	}

	/// <summary>Why <paramref name="name"/> is not a certbot lineage name, or null: letters, digits, dots, dashes and underscores.</summary>
	public static string? LineageProblem(string? name) =>
		name is not null && LineagePattern().IsMatch(name) && !name.Contains("..", StringComparison.Ordinal)
			? null
			: "A certificate name is letters, digits, dots, dashes and underscores, as certbot makes it.";

	private static bool UsesSystemd(ServerFacts server) => server.InitSystem == "systemd" && server.HasCommand(Systemctl);

	private static string Part(string name) => $"printf '%s\\n' '{Marker}{name}'\n";

	private static void ThrowIfBad(string? problem, string parameter)
	{
		if (problem is not null)
		{
			throw new ArgumentException(problem, parameter);
		}
	}

	private static string Shown(string? value) => value is null ? "" : value.Length > 60 ? value[..60] + "..." : value;

	[GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,254}\z", RegexOptions.CultureInvariant)]
	private static partial Regex LineagePattern();
}
