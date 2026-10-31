using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Moka.Certificates.Components;
using Moka.Certificates.Crypto;
using Moka.Certificates.Tls;
using Moka.Red.Core.Enums;
using Mokaterm.Abstractions.Platform;
using Mokaterm.UI.Common.Components;

namespace Moka.Certificates.Tool;

/// <summary>
/// Connects to a server from this machine, records its chain, protocol and cipher, asks after each TLS version with a
/// handshake of its own, and says in words what a client would think of it. A STARTTLS mode speaks that protocol's few
/// plain lines before the handshake. Offered on the desktop only: on the web host the connection would come from the
/// server Mokaterm runs on, which is not where the user stands and may reach networks they cannot.
/// </summary>
public sealed partial class ServerCheckSection : CertificateViewBase, IDisposable
{
	private const string SentList = "sent";
	private const string BuiltList = "built";

	private static readonly IReadOnlyList<EnumOption<StartTlsMode>> Modes =
	[
		new(StartTlsMode.Plain, "Plain"),
		new(StartTlsMode.Smtp, "SMTP"),
		new(StartTlsMode.Imap, "IMAP"),
		new(StartTlsMode.Ftp, "FTP"),
	];

	private string _host = "";
	private string _port = "";
	private string _serverName = "";
	private StartTlsMode _mode = StartTlsMode.Plain;
	private bool _busy;
	private string? _error;
	private TlsReport? _report;
	private (string List, int Position)? _selected;
	private CancellationTokenSource? _check;

	[Inject]
	private IAppEnvironment Environment { get; set; } = default!;

	[Parameter]
	public EventCallback<string> OnInspect { get; set; }

	private string Summary => _busy ? "connecting" : _report is { } report ? report.Endpoint : "TLS chain, protocol and cipher";

	private CertificateInfo? SelectedCertificate => (_report, _selected) switch
	{
		({ } report, (SentList, int position)) when position < report.Sent.Count => report.Sent[position],
		({ } report, (BuiltList, int position)) when position < report.Chain.Count => report.Chain[position],
		_ => null,
	};

	private string PortPlaceholder => TlsTarget.DefaultPort(_mode).ToString(CultureInfo.InvariantCulture);

	private string ModeHelp => _mode switch
	{
		StartTlsMode.Smtp => "The SMTP greeting, EHLO and STARTTLS are spoken before the handshake. Port 587 when none is typed.",
		StartTlsMode.Imap => "The IMAP greeting and one STARTTLS command are spoken before the handshake. Port 143 when none is typed.",
		StartTlsMode.Ftp => "The FTP greeting and AUTH TLS are spoken before the handshake. Port 21 when none is typed.",
		_ => "TLS from the first byte, as HTTPS does. Port 443 when none is typed.",
	};

	/// <summary>Why the check is not offered on this host, or null on the desktop.</summary>
	public static string? Unavailable(IAppEnvironment environment)
	{
		ArgumentNullException.ThrowIfNull(environment);
		return environment.Kind == HostKind.Desktop
			? null
			: "On the web host the server Mokaterm runs on would make the connection, not your machine, so the result would describe what that server sees. Check from the desktop app, or run openssl s_client -connect host:443 -showcerts where you are.";
	}

	public void Dispose()
	{
		_check?.Cancel();
		_check?.Dispose();
	}

	private static string NameChip(TlsReport report) => report.NameMatch switch
	{
		HostNameResult.Matches => "name matches",
		HostNameResult.CommonNameOnly => "common name only",
		_ => "wrong name",
	};

	private static string EndpointLine(TlsReport report) => string.Create(
		CultureInfo.CurrentCulture,
		$"{report.Endpoint}, {ModeName(report.StartTls)}, {(report.ServerName is { } sni ? $"SNI {sni}" : "no SNI")}, {report.Elapsed.TotalMilliseconds:N0} ms");

	private static string ModeName(StartTlsMode mode) => mode switch
	{
		StartTlsMode.Smtp => "STARTTLS after an SMTP greeting",
		StartTlsMode.Imap => "STARTTLS after an IMAP greeting",
		StartTlsMode.Ftp => "TLS after an FTP AUTH TLS",
		_ => "plain TLS",
	};

	private static string VersionChipText(TlsVersionResult version) => version.Outcome switch
	{
		TlsVersionOutcome.Refused => $"{TlsReportBuilder.ProtocolName(version.Protocol)} refused",
		TlsVersionOutcome.NotOfferedHere => $"{TlsReportBuilder.ProtocolName(version.Protocol)} unknown",
		_ => TlsReportBuilder.ProtocolName(version.Protocol),
	};

	private static MokaColor VersionColor(TlsVersionResult version) => version.Outcome switch
	{
		TlsVersionOutcome.Refused => MokaColor.Error,
		TlsVersionOutcome.NotOfferedHere => MokaColor.Surface,
		_ => MokaColor.Success,
	};

	private static string VersionChipTitle(TlsVersionResult version) => version.Outcome switch
	{
		TlsVersionOutcome.Accepted => $"The server takes {TlsReportBuilder.ProtocolName(version.Protocol)}.",
		TlsVersionOutcome.Refused => $"The server refused or did not answer {TlsReportBuilder.ProtocolName(version.Protocol)}.",
		_ => $"This machine would not offer {TlsReportBuilder.ProtocolName(version.Protocol)}, so the check cannot say whether the server would take it.",
	};

	private static string DaysText(CertificateInfo certificate) => certificate.Expiry switch
	{
		ExpiryState.Expired => "expired",
		ExpiryState.NotYetValid => "not yet valid",
		_ => string.Create(CultureInfo.CurrentCulture, $"{certificate.DaysLeft:N0} d"),
	};

	private bool IsSelected(string list, int position) => _selected is { } selected && selected.List == list && selected.Position == position;

	private void Select(string list, int position) => _selected = (list, position);

	private void CloseDetail() => _selected = null;

	private async Task OnKeyAsync(KeyboardEventArgs args)
	{
		if (args.Key == "Enter" && !_busy)
		{
			await CheckAsync();
		}
	}

	private async Task CheckAsync()
	{
		if (Unavailable(Environment) is not null)
		{
			return;
		}

		_error = null;
		if (!TlsTarget.TryParse(_host, _port, _serverName, _mode, out TlsTarget? target, out string? problem))
		{
			_error = problem;
			return;
		}

		_check?.Cancel();
		_check?.Dispose();
		CancellationTokenSource check = new();
		_check = check;
		_busy = true;
		StateHasChanged();
		try
		{
			TimeSpan timeout = TimeSpan.FromSeconds(Settings.ServerCheckTimeoutSeconds);
			using TlsObservation observation = await TlsProbe.ProbeAsync(target, timeout, check.Token);
			// The chain report comes from the unpinned handshake alone; these throw nothing away but which versions the server takes.
			IReadOnlyList<TlsVersionResult> versions = await TlsProbe.ProbeVersionsAsync(target, timeout, check.Token);
			_report = TlsReportBuilder.Build(observation, Now, Settings.ExpiringDays, versions);
			_selected = (SentList, 0);
		}
		catch (CertificateException ex)
		{
			_error = ex.Message;
			_report = null;
		}
		catch (OperationCanceledException) when (check.IsCancellationRequested)
		{
			return;
		}
		finally
		{
			if (ReferenceEquals(_check, check))
			{
				_busy = false;
			}
		}
	}
}
