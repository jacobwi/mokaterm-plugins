using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Moka.Network.Components;
using Moka.Network.Targets;
using Moka.Network.Whois;
using Mokaterm.UI.Common.PluginUi;

namespace Moka.Network.Tool;

/// <summary>
/// Whois for a domain or an address, starting at IANA and following the referrals the registries give, each answer
/// shown as the server sent it, made plain, with a copy button. When TCP port 43 gives nothing for a domain, the
/// registry's own facts are asked over RDAP instead, as facts with the raw JSON behind them.
/// </summary>
public sealed partial class WhoisSection : NetworkViewBase
{
	private readonly List<WhoisAnswer> _answers = [];
	private string _query = "";
	private string? _note;
	private string? _error;
	private RdapResult? _rdap;
	private bool _askingRdap;

	[Inject]
	private RdapClient Rdap { get; set; } = default!;

	private string Summary => Running
		? _askingRdap ? "asking the registry over RDAP" : $"asking {_answers.LastOrDefault()?.Referral ?? WhoisClient.RootServer}"
		: _rdap is { } ? "registry record over RDAP" : _answers.Count > 0 ? $"{_answers.Count} servers asked" : "who holds a domain or an address block";

	private string ServerLine
	{
		get
		{
			if (_answers.Count > 0)
			{
				return string.Join(" > ", _answers.Select(answer => answer.Server));
			}

			return _rdap is { RegistrarServer: { } registrar } rdap ? $"{rdap.Server} > {registrar} over RDAP"
				: _rdap is { } alone ? $"{alone.Server} over RDAP"
				: "";
		}
	}

	private static string Detail(WhoisAnswer answer)
	{
		StringBuilder detail = new();
		detail.Append(answer.Bytes.ToString("N0", CultureInfo.CurrentCulture)).Append(" bytes, ").Append(Milliseconds(answer.Elapsed));
		if (answer.Cut)
		{
			detail.Append(", cut at the size limit");
		}

		if (answer.TimedOut)
		{
			detail.Append(", stopped at the time limit");
		}

		if (answer.Referral is { } referral)
		{
			detail.Append(", refers to ").Append(referral);
		}

		return detail.ToString();
	}

	/// <summary>The registry's facts as the result shows them: what it has, in the order it matters.</summary>
	private static List<Fact> Facts(RdapResult rdap)
	{
		RdapDomain domain = rdap.Domain;
		List<Fact> facts = [];
		if (domain.Registrar is { } registrar)
		{
			List<string> values = [registrar];
			if (domain.RegistrarId is { } identifier)
			{
				values.Add($"IANA ID {identifier}");
			}

			facts.Add(new Fact("Registrar", values));
		}

		if (domain.Statuses.Count > 0)
		{
			facts.Add(new Fact("Status", domain.Statuses));
		}

		if (domain.Registered is { } registered)
		{
			facts.Add(new Fact("Registered", [DateText(registered)], Mono: true));
		}

		if (domain.Updated is { } updated)
		{
			facts.Add(new Fact("Updated", [DateText(updated)], Mono: true));
		}

		if (domain.Expires is { } expires)
		{
			facts.Add(new Fact("Expires", [DateText(expires)], Mono: true));
		}

		if (domain.Nameservers.Count > 0)
		{
			facts.Add(new Fact("Nameservers", domain.Nameservers, Mono: true));
		}

		return facts;
	}

	/// <summary>An RDAP date the way the view prints it: the date and time the registry reported, in UTC.</summary>
	private static string DateText(string date) =>
		DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset when)
			? when.UtcDateTime.ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture) + " UTC"
			: date;

	private async Task OnKeyAsync(KeyboardEventArgs args)
	{
		if (args.Key == "Enter" && !Busy)
		{
			await LookupAsync();
		}
	}

	private Task PrimaryAsync()
	{
		if (Running)
		{
			Stop();
			return Task.CompletedTask;
		}

		return LookupAsync();
	}

	private Task CopyAllAsync()
	{
		StringBuilder text = new();
		foreach (WhoisAnswer answer in _answers.Where(answer => answer.Error is null))
		{
			text.Append("# ").Append(answer.Server).Append('\n').Append(answer.Text.TrimEnd()).Append("\n\n");
		}

		return CopyAsync(text.ToString().TrimEnd(), "Whois answers");
	}

	private async Task LookupAsync()
	{
		_error = null;
		if (!WhoisInput.TryParse(_query, out string? query, out string? problem))
		{
			_error = problem;
			return;
		}

		Recent.Remember(query);
		TimeSpan timeout = Settings.WhoisTimeout;
		CancellationTokenSource run = BeginRun();
		_answers.Clear();
		_rdap = null;
		_askingRdap = false;
		_note = null;
		StateHasChanged();
		try
		{
			WhoisResult result = await Task.Run(
				() => WhoisClient.LookupAsync(TcpWhoisTransport.Instance, query, timeout, answer => Post(() => _answers.Add(answer)), run.Token),
				run.Token);
			_note = result.Note;
		}
		catch (ProbeException ex) when (!AddressText.TryParse(query, out _))
		{
			// Port 43 gave nothing for a domain: blocked, refused or hostile. The registries speak RDAP over HTTPS too.
			await OverRdapAsync(query, ex.Message, timeout, run);
		}
		catch (ProbeException ex)
		{
			_error = ex.Message;
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			_note = "Stopped before every server answered.";
		}
		finally
		{
			EndRun(run);
		}
	}

	/// <summary>Asks the registry over RDAP, and says plainly when that fails too.</summary>
	private async Task OverRdapAsync(string query, string port43, TimeSpan timeout, CancellationTokenSource run)
	{
		_askingRdap = true;
		StateHasChanged();
		try
		{
			RdapResult rdap = await Task.Run(() => Rdap.LookupDomainAsync(query, timeout, run.Token), run.Token);
			_rdap = rdap;
			_note = $"Port 43 gave nothing ({port43}), so the registry was asked over RDAP instead.";
		}
		catch (ProbeException ex)
		{
			_error = $"Port 43 gave nothing ({port43}), and RDAP over HTTPS failed too: {ex.Message}";
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			_note = "Stopped before the registry answered over RDAP.";
		}
		finally
		{
			_askingRdap = false;
		}
	}
}
