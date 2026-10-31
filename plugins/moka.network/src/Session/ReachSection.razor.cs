using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using Moka.Network.Server;
using Moka.Network.Targets;

namespace Moka.Network.Session;

/// <summary>
/// Can the server reach a host: a TCP check through bash's <c>/dev/tcp</c>, a run of <c>ping</c>, and
/// <c>tracepath</c> or <c>traceroute</c> for the path, each its own action with its own wait. Every one of them probes
/// for the tool it needs first and says so plainly when the server does not have it; each command is built from
/// checked fields and quoted, so nothing typed ever becomes part of a shell line unquoted.
/// </summary>
public sealed partial class ReachSection : SessionSectionBase
{
	private string _host = "";
	private string _port = "443";
	private string _seconds = "5";
	private string _count = "4";
	private string? _error;
	private string? _tcpTarget;
	private string _pingTarget = "";
	private string _traceTarget = "";
	private string _pingText = "";
	private string _traceText = "";
	private string _tracer = "";
	private ReachVerdict? _tcp;
	private bool _probed;
	private bool _hasBash;
	private bool _hasPing;
	private bool _hasTracePath;
	private bool _hasTraceroute;

	private bool Offline => Session is not null && !Connected;

	private bool NotReady => !Connected;

	private bool CanPing => Connected && _probed && _hasPing && !Busy;

	private bool CanTrace => Connected && _probed && (_hasTracePath || _hasTraceroute) && !Busy;

	private bool HasTracePath => _hasTracePath;

	private bool HasTraceroute => _hasTraceroute && !_hasTracePath;

	private string Summary => !_probed
		? "Asking the server what it has"
		: "TCP, ping and the path, from the server's side";

	private string PrimaryText => Running ? "Stop" : "Check";

	private string? PrimaryTitle => !Connected
		? "The session is not connected."
		: _probed && !_hasBash ? "This server has no bash, which the TCP check reads /dev/tcp from."
		: null;

	protected override void OnSessionChange()
	{
		if (Connected && !_probed)
		{
			_ = ProbeAsync();
		}
	}

	protected override void OnAfterRender(bool firstRender)
	{
		if (firstRender && Connected && !_probed)
		{
			_ = ProbeAsync();
		}
	}

	private async Task ProbeAsync()
	{
		if (Runner is not { } runner)
		{
			return;
		}

		_probed = true;
		IReadOnlySet<string> present = await runner.ProgramsAsync(["bash", "ping", "tracepath", "traceroute"], Settings.ServerTimeout);
		Post(() =>
		{
			_hasBash = present.Contains("bash");
			_hasPing = present.Contains("ping");
			_hasTracePath = present.Contains("tracepath");
			_hasTraceroute = present.Contains("traceroute");
		});
	}

	private async Task OnKeyAsync(KeyboardEventArgs args)
	{
		if (args.Key == "Enter" && !Busy)
		{
			await CheckAsync();
		}
	}

	private Task PrimaryAsync()
	{
		if (Running)
		{
			Stop();
			return Task.CompletedTask;
		}

		return CheckAsync();
	}

	private async Task CheckAsync()
	{
		await RunToolAsync(_hasBash, () => "This server has no bash, which the TCP check reads /dev/tcp from.",
			async (host, run) =>
			{
				int? port = ReadNumber(_port, 1, 65535);
				int? seconds = ReadNumber(_seconds, ServerReach.MinTcpSeconds, ServerReach.MaxTcpSeconds);
				if (port is null || seconds is null)
				{
					_error = port is null
						? "Port is a number from 1 to 65535."
						: $"Wait is a number of seconds from {ServerReach.MinTcpSeconds} to {ServerReach.MaxTcpSeconds}.";
					return null;
				}

				string command = ServerReach.TcpCommand(host.Host, port.Value, seconds.Value);
				_tcpTarget = $"{host.Host}:{port.Value} from the server";
				return await run.RunAsync(command, TimeSpan.FromSeconds(seconds.Value + 3));
			},
			answer =>
			{
				_tcp = ServerReach.TcpVerdict(answer);
			});
	}

	private async Task PingAsync()
	{
		await RunToolAsync(_hasPing, () => "This server has no ping.",
			async (host, run) =>
			{
				int? count = ReadNumber(_count, ServerReach.MinPingCount, ServerReach.MaxPingCount);
				if (count is null)
				{
					_error = $"Pings is a number from {ServerReach.MinPingCount} to {ServerReach.MaxPingCount}.";
					return null;
				}

				string command = ServerReach.PingCommand(host.Host, count.Value);
				_pingTarget = $"{host.Host} ({count.Value} requests)";
				return await run.RunAsync(command, ServerReach.PingRunTimeout(count.Value, ServerReach.PingReplyWaitSeconds));
			},
			answer =>
			{
				_pingText = answer.Text;
			});
	}

	private async Task TraceAsync()
	{
		await RunToolAsync(_hasTracePath || _hasTraceroute, () => "This server has neither tracepath nor traceroute.",
			async (host, run) =>
			{
				string command = _hasTracePath ? ServerReach.TracePathCommand(host.Host) : ServerReach.TracerouteCommand(host.Host);
				_tracer = _hasTracePath ? "tracepath" : "traceroute";
				_traceTarget = host.Host;
				return await run.RunAsync(command, ServerReach.TraceRunTimeout);
			},
			answer =>
			{
				_traceText = answer.Text;
			});
	}

	/// <summary>One action's whole shape: guard the tool, read the host, run the command with its own wait, show what came back.</summary>
	private async Task RunToolAsync(bool toolPresent, Func<string> absentWord, Func<HostTarget, ServerRunner, Task<ServerRun?>> action, Action<ServerRun> show)
	{
		_error = null;
		if (Runner is not { } runner)
		{
			return;
		}

		if (!toolPresent)
		{
			_error = absentWord();
			return;
		}

		if (!HostInput.TryParse(_host, out HostTarget? target, out string? problem))
		{
			_error = problem;
			return;
		}

		CancellationTokenSource run = BeginRun();
		StateHasChanged();
		try
		{
			ServerRun? answer = await action(target, runner);
			if (answer is not null)
			{
				Post(() =>
				{
					if (answer.NeverRan || answer.TimedOut || answer.Broke)
					{
						_error = answer.Problem;
					}
					else
					{
						show(answer);
					}
				});
			}
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			// Stopped: whatever arrived stays.
		}
		finally
		{
			EndRun(run);
		}
	}

	/// <summary>A whole number typed into a small field, or null when it does not read or falls outside the range.</summary>
	private static int? ReadNumber(string text, int min, int max) =>
		int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.CurrentCulture, out int value) && value >= min && value <= max ? value : null;
}
