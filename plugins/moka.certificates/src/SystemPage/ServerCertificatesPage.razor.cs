using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Moka.Certificates.Crypto;
using Moka.Certificates.ServerFiles;
using Moka.Certificates.Settings;
using Moka.Red.Core.Enums;
using Mokaterm.Abstractions.ServerAdmin;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.ToolPages;

namespace Moka.Certificates.SystemPage;

/// <summary>
/// The certificates a server uses: certbot's lineages, the files nginx and Apache name, and the extra paths from
/// settings, each read through the view's script runner and described by the same reader the tool uses. Renewals and
/// reloads are reviewed changes, as root.
/// </summary>
public sealed partial class ServerCertificatesPage : SystemPageBase
{
	private DiscoveryResult _discovery = new();
	private IReadOnlyList<ServerCertificateFile> _files = [];
	private List<ServerCertificateFile> _shown = [];
	private string _filter = "";
	private string? _selected;
	private int _certificateIndex;
	private CertificateSettings _settings = new();

	/// <summary>certbot and the live folders answer root only, so root mode brings more.</summary>
	protected override bool ReadsMoreAsRoot => true;

	[Inject]
	private ISettingsService SettingsService { get; set; } = default!;

	[Inject]
	private TimeProvider Time { get; set; } = default!;

	private bool HasCertbot => Context.Server.HasCommand(ServerCertificateCommands.Certbot);

	private ServerCertificateFile? Selected => _selected is { } path ? _files.FirstOrDefault(file => file.Path == path) : null;

	private string Summary
	{
		get
		{
			int read = _files.Count(file => file.Status == ServerFileStatus.Read);
			int expiring = _files.Count(file => file.Leaf?.Expiry == ExpiryState.Expiring);
			int expired = _files.Count(file => file.Leaf?.Expiry == ExpiryState.Expired);
			return string.Create(CultureInfo.CurrentCulture, $"{read} read, {expiring} expiring, {expired} expired");
		}
	}

	private IEnumerable<(string Label, int Count, bool Alert)> Counts =>
	[
		("Expired", _files.Count(file => file.Leaf?.Expiry == ExpiryState.Expired), true),
		("Expiring", _files.Count(file => file.Leaf?.Expiry == ExpiryState.Expiring), true),
		("Not read", _files.Count(file => file.Status != ServerFileStatus.Read), false),
	];

	private string? RootHint
	{
		get
		{
			if (Context.CanReadAsRoot)
			{
				return null;
			}

			List<string> parts = [];
			if (_discovery.CertbotNeedsRoot)
			{
				parts.Add("certbot lists its certificates to root only");
			}

			if (_discovery.LiveNeedsRoot)
			{
				parts.Add("/etc/letsencrypt/live is root's");
			}

			if (_discovery.NginxFailed)
			{
				parts.Add("nginx shows its configuration to root only here");
			}

			if (_files.Any(file => file.Status == ServerFileStatus.Unreadable))
			{
				parts.Add("some files are root's");
			}

			return parts.Count == 0 ? null : $"Root mode reads more: {string.Join(", ", parts)}.";
		}
	}

	private string LookedIn
	{
		get
		{
			List<string> places = [];
			places.Add(HasCertbot ? "certbot certificates" + (Context.CanReadAsRoot ? "" : " (needs root mode)") : "certbot (not installed)");
			places.Add("/etc/letsencrypt/live");
			places.Add(Context.Server.HasCommand(ServerCertificateCommands.Nginx) ? "nginx -T" : "nginx (not installed)");
			places.Add("SSLCertificateFile lines under " + string.Join(", ", ServerCertificateCommands.ApacheFolders));
			int extra = _settings.ExtraPathList().Count;
			places.Add(extra == 0 ? "no extra paths (add some in Settings, Plugins, Certificates)" : string.Create(CultureInfo.CurrentCulture, $"{extra} extra paths from Settings"));
			return "Looked in " + string.Join("; ", places) + ".";
		}
	}

	protected override async Task LoadAsync(CancellationToken cancellationToken)
	{
		_settings = SettingsService.Get<CertificateSettings>().Clamped();
		bool asRoot = ReadAsRoot;
		SystemScriptResult discovered = await ReadAsync(ServerCertificateCommands.Discover(Context.Server, _settings.ExtraPathList(), asRoot), cancellationToken);
		if (!discovered.Succeeded)
		{
			throw new SystemScriptException($"The certificates could not be looked up: {discovered.Problem}");
		}

		_discovery = ScriptOutput.ParseDiscovery(discovered.Output);
		IReadOnlyList<ScanCandidate> candidates = ServerCertificateScan.Candidates(_discovery);
		IReadOnlyDictionary<int, FileRead> reads = new Dictionary<int, FileRead>();
		if (candidates.Count > 0)
		{
			SystemScriptResult read = await ReadAsync(ServerCertificateCommands.ReadFiles(Context.Server, [.. candidates.Select(candidate => candidate.Path)], asRoot), cancellationToken);
			if (!read.Succeeded)
			{
				throw new SystemScriptException($"The certificate files could not be read: {read.Problem}");
			}

			reads = ScriptOutput.ParseFiles(read.Output);
		}

		_files = [.. ServerCertificateScan.Build(candidates, reads, asRoot, Time.GetUtcNow(), _settings.ExpiringDays)
			.OrderBy(file => file.Leaf is null)
			.ThenBy(file => file.Leaf?.NotAfter ?? DateTimeOffset.MaxValue)];
		ApplyFilter();
	}

	private static MokaColor DotOf(ServerCertificateFile file) => file.Leaf is { } leaf
		? leaf.Expiry switch
		{
			ExpiryState.Expired or ExpiryState.NotYetValid => MokaColor.Error,
			ExpiryState.Expiring => MokaColor.Warning,
			_ => MokaColor.Success,
		}
		: MokaColor.Surface;

	private static bool GlowOf(ServerCertificateFile file) => file.Leaf?.Expiry is ExpiryState.Expired or ExpiryState.Expiring or ExpiryState.NotYetValid;

	private static string TitleOf(ServerCertificateFile file) => file.Leaf?.Title ?? file.Lineage?.Name ?? Path.GetFileName(file.Path);

	private static string ExpiresOf(ServerCertificateFile file) => file.Leaf switch
	{
		null => file.Status switch
		{
			ServerFileStatus.Unreadable => "no access",
			ServerFileStatus.Missing => "missing",
			_ => "-",
		},
		{ Expiry: ExpiryState.Expired } => "expired",
		{ } leaf => string.Create(CultureInfo.CurrentCulture, $"{leaf.NotAfter.UtcDateTime:yyyy-MM-dd}"),
	};

	private static string UsedBy(ServerCertificateFile file) => string.Join(", ", file.Uses.Select(use => use.Source).Distinct(StringComparer.Ordinal));

	private static bool UsedByNginx(ServerCertificateFile file) => file.Uses.Any(use => use.Source.StartsWith("nginx", StringComparison.Ordinal));

	private static bool UsedByApache(ServerCertificateFile file) => file.Uses.Any(use => use.Source == "Apache");

	private CertificateInfo? ShownCertificate(ServerCertificateFile file) =>
		file.Certificates.Count == 0 ? null : file.Certificates[Math.Clamp(_certificateIndex, 0, file.Certificates.Count - 1)];

	private void OnFilterChanged(string filter)
	{
		_filter = filter;
		ApplyFilter();
	}

	private void ApplyFilter()
	{
		string filter = _filter.Trim();
		_shown = filter.Length == 0
			? [.. _files]
			: [.. _files.Where(file =>
				file.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)
				|| (file.Leaf is { } leaf && (leaf.Title.Contains(filter, StringComparison.OrdinalIgnoreCase)
					|| leaf.Issuer.Contains(filter, StringComparison.OrdinalIgnoreCase)
					|| leaf.AlternativeNames.Any(name => name.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))))
				|| file.Uses.Any(use => use.Detail.Contains(filter, StringComparison.OrdinalIgnoreCase)))];
	}

	private void Select(ServerCertificateFile file)
	{
		_selected = file.Path == _selected ? null : file.Path;
		_certificateIndex = 0;
	}

	private void OnRowKey(KeyboardEventArgs args, ServerCertificateFile file)
	{
		if (args.Key is "Enter" or " ")
		{
			Select(file);
		}
	}

	private void CloseDetail() => _selected = null;

	private Task EnableRootAsync() => Context.EnableRootAsync();

	private async Task TestAllAsync() => await RunChangeAsync(ServerCertificateCommands.RenewDryRun(null));

	private async Task TestOneAsync(string lineage)
	{
		if (ServerCertificateCommands.LineageProblem(lineage) is null)
		{
			await RunChangeAsync(ServerCertificateCommands.RenewDryRun(lineage));
		}
	}

	private async Task RenewAsync(string lineage)
	{
		if (ServerCertificateCommands.LineageProblem(lineage) is null)
		{
			await RunChangeAsync(ServerCertificateCommands.Renew(lineage));
		}
	}

	private async Task ReloadNginxAsync() => await RunChangeAsync(ServerCertificateCommands.ReloadNginx(Context.Server));

	private async Task ReloadApacheAsync()
	{
		if (ServerCertificateCommands.ReloadApache(Context.Server) is { } change)
		{
			await RunChangeAsync(change);
		}
	}
}
