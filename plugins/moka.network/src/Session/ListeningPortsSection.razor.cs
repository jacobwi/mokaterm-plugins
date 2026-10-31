using Moka.Network.Server;

namespace Moka.Network.Session;

/// <summary>
/// The ports the server itself is listening on, from <c>ss -tulpn</c>: which address and port each tcp and udp socket
/// sits on and which process holds it, as far as the logged-in account may see. The tab probes for ss once it lands
/// on a connected session and says so plainly when the server does not have it, rather than showing an empty table.
/// </summary>
public sealed partial class ListeningPortsSection : SessionSectionBase
{
	private IReadOnlyList<SsRow> _rows = [];
	private string? _error;
	private bool _probed;
	private bool _hasSs;

	private bool Offline => Session is not null && !Connected;

	private bool _absent => _probed && !_hasSs;

	private bool _hiddenProcesses => _rows.Count > 0 && _rows.All(row => row.Process.Length == 0);

	private string Summary => !_probed
		? "Asking the server what it has"
		: _hasSs
			? _rows.Count > 0 ? $"{_rows.Count} listening sockets on the server" : "no listening sockets found"
			: "no ss on this server";

	private string? PrimaryTitle => !Connected
		? "The session is not connected."
		: _absent ? "This server has no ss."
		: null;

	protected override void OnSessionChange()
	{
		if (Connected && !_probed)
		{
			_ = ReadAsync();
		}
	}

	protected override void OnAfterRender(bool firstRender)
	{
		if (firstRender && Connected && !_probed)
		{
			_ = ReadAsync();
		}
	}

	private async Task PrimaryAsync()
	{
		if (Running)
		{
			Stop();
			return;
		}

		await ReadAsync();
	}

	private async Task ReadAsync()
	{
		if (Runner is not { } runner)
		{
			return;
		}

		_error = null;
		_probed = true;
		CancellationTokenSource run = BeginRun();
		StateHasChanged();
		try
		{
			IReadOnlySet<string> present = await runner.ProgramsAsync(["ss"], Settings.ServerTimeout, run.Token);
			if (!present.Contains("ss"))
			{
				Post(() =>
				{
					_hasSs = false;
					_rows = [];
				});
				return;
			}

			ServerRun listing = await runner.RunAsync(ServerQuote.Line(["ss", "-tulpn"]), Settings.ServerTimeout, ServerRunner.ListingOutputBytes, cancellationToken: run.Token);
			Post(() =>
			{
				_hasSs = true;
				if (listing.Ok)
				{
					_rows = SsListing.Parse(listing.Output);
				}
				else
				{
					_rows = [];
					_error = listing.NeverRan || listing.TimedOut || listing.Broke
						? listing.Problem
						: $"ss ended with status {listing.ExitCode}." + (listing.Text.Length > 0 ? $" {FirstLine(listing.Text)}" : "");
				}
			});
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			// Stopped: the listing so far stays.
		}
		finally
		{
			EndRun(run);
		}
	}

	private static string FirstLine(string text)
	{
		int end = text.IndexOf('\n');
		return (end < 0 ? text : text[..end]).Trim();
	}

	private static string Process(SsRow row) => row.Process.Length > 0 ? row.Process : "-";
}
