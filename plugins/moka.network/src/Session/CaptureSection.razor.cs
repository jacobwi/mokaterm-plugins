using Microsoft.AspNetCore.Components.Web;
using Moka.Network.Server;
using Mokaterm.UI.Common.Components;

namespace Moka.Network.Session;

/// <summary>
/// A packet capture on the server: tcpdump under a filter built from three checked fields, its lines parsed into a
/// table as they arrive, the newest at the bottom. A capture runs until it is stopped, so it is the one command the
/// tab marks as long; a hidden tab keeps the capture going and only stops redrawing. tcpdump needs root on most
/// servers, and when the server says no, the tab says whose problem that is in plain words.
/// </summary>
public sealed partial class CaptureSection : SessionSectionBase
{
	private static readonly IReadOnlyList<EnumOption<CaptureProtocol>> Protocols =
	[
		new(CaptureProtocol.Tcp, "TCP"),
		new(CaptureProtocol.Udp, "UDP"),
		new(CaptureProtocol.Icmp, "ICMP"),
	];

	private string _host = "";
	private string _port = "";
	private CaptureProtocol _protocol = CaptureProtocol.Tcp;
	private string? _error;
	private string? _refused;
	private string _filter = "";
	private bool _probed;
	private bool _hasTcpDump;
	private CaptureLines _lines = new();
	private IReadOnlyList<CaptureEntry> _rows = [];
	private int _drawn;
	private System.Threading.Timer? _redraw;

	private bool Capturing => Running;

	private bool _absent => _probed && !_hasTcpDump;

	private string Summary => Capturing
		? $"Capturing: {_filter}"
		: _probed
			? _hasTcpDump ? $"{Count(_lines.Total)} lines so far" : "no tcpdump on this server"
			: "Asking the server what it has";

	private string PrimaryText => Capturing ? "Stop" : "Start";

	private string? PrimaryTitle => !Connected
		? "The session is not connected."
		: _absent ? "This server has no tcpdump."
		: null;

	protected override void OnParametersSet()
	{
		// The redraw clock follows the tab being on show: a hidden tab keeps capturing and draws nothing until it is
		// the one on show again.
		if (!IsActive)
		{
			StopRedraw();
		}
		else if (Capturing && _redraw is null)
		{
			StartRedraw();
		}
	}

	private Task PrimaryAsync()
	{
		return Capturing ? StopAsync() : StartAsync();
	}

	private async Task OnKeyAsync(KeyboardEventArgs args)
	{
		if (args.Key == "Enter" && !Busy)
		{
			await StartAsync();
		}
	}

	private Task StopAsync()
	{
		Stop();
		return Task.CompletedTask;
	}

	private async Task StartAsync()
	{
		_error = null;
		_refused = null;
		if (Runner is not { } runner)
		{
			return;
		}

		if (TcpDumpFilter.TryRead(_host, _port, _protocol, out string? problem) is not { } filter)
		{
			_error = problem;
			return;
		}

		if (!_probed)
		{
			_probed = true;
			IReadOnlySet<string> present = await runner.ProgramsAsync(["tcpdump"], Settings.ServerTimeout);
			_hasTcpDump = present.Contains("tcpdump");
			if (!_hasTcpDump)
			{
				StateHasChanged();
				return;
			}
		}
		else if (!_hasTcpDump)
		{
			return;
		}

		CaptureLines lines = new();
		CancellationTokenSource run = BeginRun();
		_lines = lines;
		_rows = [];
		_drawn = 0;
		_filter = filter.Expression;
		_refused = null;
		StateHasChanged();
		StartRedraw();
		try
		{
			string command = TcpDumpCapture.Command(filter);
			ServerRun ended = await runner.RunAsync(
				command,
				Timeout.InfiniteTimeSpan,
				ServerRunner.CaptureOutputBytes,
				runsLong: true,
				lines.Write,
				run.Token);
			lines.Complete();
			Post(() =>
			{
				_rows = lines.Snapshot();
				_drawn = lines.Version;
				_refused = TcpDumpCapture.Refusal(ended);
			});
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			// Stopped by the one who started it: the lines stay, and the counts tell the rest.
			lines.Complete();
			Post(() =>
			{
				_rows = lines.Snapshot();
				_drawn = lines.Version;
			});
		}
		finally
		{
			EndRun(run);
			Post(StopRedraw);
		}
	}

	/// <summary>Ticks while a capture runs on the tab that is on show, redrawing only when the ring moved.</summary>
	private void StartRedraw()
	{
		_redraw?.Dispose();
		_redraw = new System.Threading.Timer(_ => Redraw(), null, TcpDumpCapture.RedrawEvery, TcpDumpCapture.RedrawEvery);
	}

	private void StopRedraw()
	{
		_redraw?.Dispose();
		_redraw = null;
	}

	private void Redraw()
	{
		if (!IsActive || !Capturing || Disposed)
		{
			return;
		}

		if (_lines.Version == _drawn)
		{
			return;
		}

		_ = InvokeAsync(() =>
		{
			if (Disposed || !IsActive)
			{
				return;
			}

			_rows = _lines.Snapshot();
			_drawn = _lines.Version;
			StateHasChanged();
		});
	}

	protected override void Dispose(bool disposing)
	{
		StopRedraw();
		base.Dispose(disposing);
	}

	private static string Protocol(CaptureEntry entry)
	{
		string proto = entry.Parsed.Protocol;
		return entry.Parsed.Length.Length > 0 ? (proto.Length > 0 ? $"{proto} {entry.Parsed.Length}" : entry.Parsed.Length) : proto;
	}
}
