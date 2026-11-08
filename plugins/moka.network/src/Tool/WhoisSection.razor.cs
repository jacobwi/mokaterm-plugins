using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Moka.Network.Components;
using Moka.Network.Targets;
using Moka.Network.Whois;

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
		? RunningText
		: _rdap is { } ? "Registry record over RDAP" : _answers.Count > 0 ? NetworkFormat.Plural(_answers.Count, "server") + " asked" : "Who holds a domain or an address block";

	private string RunningText => _askingRdap ? "Asking the registry over RDAP" : $"Asking {_answers.LastOrDefault()?.Referral ?? WhoisClient.RootServer}";

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
