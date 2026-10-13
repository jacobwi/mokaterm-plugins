using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components.Web;
using Moka.Network.Components;
using Moka.Network.Targets;
using Moka.Network.Whois;

namespace Moka.Network.Tool;

/// <summary>
/// Whois for a domain or an address, starting at IANA and following the referrals the registries give, each answer
/// shown as the server sent it, made plain, with a copy button.
/// </summary>
public sealed partial class WhoisSection : NetworkViewBase
{
	private readonly List<WhoisAnswer> _answers = [];
	private string _query = "";
	private string? _note;
	private string? _error;

	private string Summary => Running
		? $"asking {_answers.LastOrDefault()?.Referral ?? WhoisClient.RootServer}"
		: _answers.Count > 0 ? $"{_answers.Count} servers asked" : "who holds a domain or an address block";

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

		TimeSpan timeout = Settings.WhoisTimeout;
		CancellationTokenSource run = BeginRun();
		_answers.Clear();
		_note = null;
		StateHasChanged();
		try
		{
			WhoisResult result = await Task.Run(
				() => WhoisClient.LookupAsync(TcpWhoisTransport.Instance, query, timeout, answer => Post(() => _answers.Add(answer)), run.Token),
				run.Token);
			_note = result.Note;
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
}
