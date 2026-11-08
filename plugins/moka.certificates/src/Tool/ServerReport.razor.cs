using System.Globalization;
using Microsoft.AspNetCore.Components;
using Moka.Certificates.Components;
using Moka.Certificates.Crypto;
using Moka.Certificates.Tls;
using Moka.Red.Core.Enums;
using Moka.Red.Primitives.ChainCheck;
using Moka.Red.Primitives.DataList;
using Mokaterm.UI.Common.Icons;

namespace Moka.Certificates.Tool;

/// <summary>
/// One server check's report, drawn from a <see cref="TlsReport"/> alone so a test can render it from a handshake recorded
/// by hand. The server's own certificate is picked whenever a new report arrives.
/// </summary>
public sealed partial class ServerReport : CertificateViewBase
{
	private TlsReport? _shown;
	private IReadOnlyList<CertificateRow> _sentRows = [];
	private IReadOnlyList<CertificateRow> _builtRows = [];
	private CertificateRow? _selected;

	[Parameter, EditorRequired]
	public TlsReport Report { get; set; } = default!;

	/// <summary>Opens the chain the server sent in the Inspect section.</summary>
	[Parameter]
	public EventCallback<string> OnInspect { get; set; }

	private bool OldProtocol => Report.Protocol is "TLS 1.0" or "TLS 1.1" or "SSL 3.0" or "SSL 2.0";

	private string DaysLeft => Report.Leaf.Expiry switch
	{
		ExpiryState.Expired => "Expired",
		ExpiryState.NotYetValid => "Not yet",
		_ => Report.Leaf.DaysLeft.ToString("N0", CultureInfo.CurrentCulture),
	};

	private string EndpointLine => string.Create(
		CultureInfo.CurrentCulture,
		$"{Report.Endpoint}, {ModeName(Report.StartTls)}, {(Report.ServerName is { } sni ? $"SNI {sni}" : "no SNI")}, {Report.Elapsed.TotalMilliseconds:N0} ms");

	private IReadOnlyList<MokaDataListItem> ConnectionFacts =>
	[
		Facts.Mono("Endpoint", Report.Endpoint),
		Facts.Text("TLS starts", Sentence(ModeName(Report.StartTls))),
		Facts.Mono("SNI", Report.ServerName ?? "None sent"),
		Facts.Mono("Name checked", Report.NameChecked),
		Facts.Mono("Protocol", Report.Protocol),
		Facts.Mono("Cipher", Report.Cipher, "Copy the cipher suite"),
	];

	private IReadOnlyList<MokaChainStep> Steps
	{
		get
		{
			List<MokaChainStep> steps =
			[
				new(MokatermIcons.Globe, "Connect", MokaChainTone.Ok, Report.StartTls == StartTlsMode.Plain ? "Direct" : "STARTTLS",
					$"Reached {Report.Endpoint}, {ModeName(Report.StartTls)}."),
				new(MokatermIcons.Lock, "Handshake", OldProtocol ? MokaChainTone.Warn : MokaChainTone.Ok, Report.Protocol,
					$"{Report.Protocol} with {Report.Cipher}.", Report.VersionSummary),
				NameStep(),
				DatesStep(),
				TrustStep(),
			];

			if (Report.SentOrder is { } order)
			{
				steps.Add(new MokaChainStep(CertificateIcons.Certificate, "Order", order.InOrder ? MokaChainTone.Ok : MokaChainTone.Warn,
					order.InOrder ? "In order" : "Out of order", order.Summary));
			}

			return steps;
		}
	}

	protected override void OnParametersSet()
	{
		if (ReferenceEquals(_shown, Report))
		{
			return;
		}

		_shown = Report;
		_sentRows = CertificateRow.Of(Report.Sent);
		_builtRows = CertificateRow.Of(Report.Chain);
		_selected = _sentRows.Count > 0 ? _sentRows[0] : null;
	}

	private static string ModeName(StartTlsMode mode) => mode switch
	{
		StartTlsMode.Smtp => "STARTTLS after an SMTP greeting",
		StartTlsMode.Imap => "STARTTLS after an IMAP greeting",
		StartTlsMode.Ftp => "TLS after an FTP AUTH TLS",
		_ => "plain TLS",
	};

	private static string OutcomeWord(TlsVersionResult version) => version.Outcome switch
	{
		TlsVersionOutcome.Accepted => "Takes it",
		TlsVersionOutcome.Refused => "Refused",
		_ => "Unknown",
	};

	private static MokaColor OutcomeColor(TlsVersionResult version) => version.Outcome switch
	{
		TlsVersionOutcome.Refused => MokaColor.Error,
		TlsVersionOutcome.NotOfferedHere => MokaColor.Surface,
		_ => MokaColor.Success,
	};

	private static string OutcomeSentence(TlsVersionResult version) => version.Outcome switch
	{
		TlsVersionOutcome.Accepted => $"The server takes {TlsReportBuilder.ProtocolName(version.Protocol)}.",
		TlsVersionOutcome.Refused => $"The server refused or did not answer {TlsReportBuilder.ProtocolName(version.Protocol)}.",
		_ => $"This machine would not offer {TlsReportBuilder.ProtocolName(version.Protocol)}, so the check cannot say whether the server would take it.",
	};

	private static string CertificateCount(int count) => count == 1 ? "1 certificate" : string.Create(CultureInfo.CurrentCulture, $"{count} certificates");

	private MokaChainStep NameStep() => Report.NameMatch switch
	{
		HostNameResult.Matches => new(CertificateIcons.Inspect, "Name", MokaChainTone.Ok, "Matches", $"The certificate is for {Report.NameChecked}."),
		HostNameResult.CommonNameOnly => new(CertificateIcons.Inspect, "Name", MokaChainTone.Fail, "Common name only",
			$"Only the common name matches {Report.NameChecked}, and browsers ignore the common name."),
		_ => new(CertificateIcons.Inspect, "Name", MokaChainTone.Fail, "Wrong name", $"The certificate is not for {Report.NameChecked}."),
	};

	private MokaChainStep DatesStep()
	{
		CertificateInfo leaf = Report.Leaf;
		string sentence = $"It {CertificateReport.ExpiryText(leaf)}.";
		return leaf.Expiry switch
		{
			ExpiryState.Expired => new(MokatermIcons.History, "Dates", MokaChainTone.Fail, "Expired", sentence),
			ExpiryState.NotYetValid => new(MokatermIcons.History, "Dates", MokaChainTone.Fail, "Not yet valid", sentence),
			ExpiryState.Expiring => new(MokatermIcons.History, "Dates", MokaChainTone.Warn, "Expiring", sentence),
			_ => new(MokatermIcons.History, "Dates", MokaChainTone.Ok, CertificateTable.DaysText(leaf) + " left", sentence),
		};
	}

	private MokaChainStep TrustStep()
	{
		if (Report.ChainTrusted)
		{
			string root = Report.Chain.Count > 0 ? Report.Chain[^1].Title : "a trusted root";
			return new(MokatermIcons.ShieldCheck, "Trust", MokaChainTone.Ok, "Trusted", $"This machine trusts the chain up to {root}.");
		}

		return new(MokatermIcons.ShieldCheck, "Trust", MokaChainTone.Fail, "Not trusted", "This machine does not trust the chain the server sent.",
			"The problems below say why.");
	}

	private CertificateRow? SelectedIn(IReadOnlyList<CertificateRow> rows) =>
		_selected is { } selected && rows.Any(row => ReferenceEquals(row, selected)) ? selected : null;

	private void Select(CertificateRow row) => _selected = row;

	private void CloseDetail() => _selected = null;
}
