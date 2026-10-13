using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Moka.Certificates.Components;
using Moka.Certificates.Crypto;
using Moka.Certificates.Tls;
using Mokaterm.Abstractions.Platform;

namespace Moka.Certificates.Tool;

/// <summary>
/// Connects to a server from this machine, records its chain, protocol and cipher, and says in words what a client
/// would think of it. Offered on the desktop only: on the web host the connection would come from the server Mokaterm
/// runs on, which is not where the user stands and may reach networks they cannot.
/// </summary>
public sealed partial class ServerCheckSection : CertificateViewBase, IDisposable
{
	private const string SentList = "sent";
	private const string BuiltList = "built";

	private string _host = "";
	private string _port = "";
	private string _serverName = "";
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
		$"{report.Endpoint}, {(report.ServerName is { } sni ? $"SNI {sni}" : "no SNI")}, {report.Elapsed.TotalMilliseconds:N0} ms");

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
		if (!TlsTarget.TryParse(_host, _port, _serverName, out TlsTarget? target, out string? problem))
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
			_report = TlsReportBuilder.Build(observation, Now, Settings.ExpiringDays);
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
