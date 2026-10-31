using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Components.Web;
using Moka.Network.Components;
using Moka.Network.Echo;
using Moka.Network.Settings;
using Moka.Network.Targets;
using Mokaterm.UI.Common.PluginUi;

namespace Moka.Network.Tool;

/// <summary>
/// Pings a host a set number of times or until stopped, one request per interval, with a line per reply and the
/// summary kept up to date as replies arrive. The requests are ICMP echo by default, or TCP connection attempts to one
/// port for a host that drops ICMP; beside the run, a DF-set binary search finds the largest packet the path carries.
/// The list keeps the newest <see cref="MaxRows"/>; the summary counts all.
/// </summary>
public sealed partial class PingSection : NetworkViewBase
{
	public const int MaxRows = 500;

	private readonly List<PingRow> _rows = [];
	private string _host = "";
	private string _count = "4";
	private string _interval = "1";
	private string _timeout = "";
	private string _size = EchoRequest.DefaultPayload.ToString(CultureInfo.InvariantCulture);
	private string _port = "";
	private bool _continuous;
	private PingMode _mode;
	private AddressChoice _choice;
	private PingStatistics _stats = new();
	private int _dropped;
	private string? _target;
	private string? _error;
	private PathMtuResult? _mtu;

	private string Summary => Running
		? $"{_stats.Sent} sent"
		: _stats.Sent > 0 ? $"{_stats.Received} of {_stats.Sent} answered" : _mode == PingMode.Tcp ? "TCP connection attempts, one at a time" : "ICMP echo, one request at a time";

	private string LossText => _stats.LossPercent.ToString(_stats.LossPercent is 0 or 100 ? "0" : "0.#", CultureInfo.CurrentCulture) + "%";

	protected override void OnInitialized()
	{
		_timeout = Settings.PingTimeoutMilliseconds.ToString(CultureInfo.CurrentCulture);
		_port = Settings.TcpPingPort.ToString(CultureInfo.CurrentCulture);
	}

	private static string Rtt(double? milliseconds) => milliseconds is { } value ? Milliseconds(value) : "-";

	private static string ResultText(EchoReply reply) => reply.Status switch
	{
		EchoStatus.Reply => reply.Detail is { } detail ? $"reply ({detail})" : "reply",
		EchoStatus.TimedOut => "timed out",
		EchoStatus.TtlExpired => "TTL expired on the way",
		EchoStatus.Unreachable => $"unreachable: {reply.Detail}",
		_ => $"failed: {reply.Detail}",
	};

	private static string ResultClass(EchoReply reply) => reply.Status switch
	{
		EchoStatus.Reply => "mn-td",
		EchoStatus.TimedOut => "mn-td mn-td--warn",
		_ => "mn-td mn-td--bad",
	};

	private static string MtuClass(PathMtuResult mtu) => mtu.LargestPayload is null ? "mn-result-line mn-result-line--bad" : "mn-result-line mn-result-line--good";

	private static string MtuText(PathMtuResult mtu) => mtu.AtCeiling
		? $"Every size up to {PathMtu.MaxPayload} bytes passed, so the path carries at least the classic {PathMtu.MaxPayload + PathMtu.HeaderBytes} byte packet whole; whether it carries more cannot be seen from here."
		: mtu.LargestPayload is { } largest
			? $"Packets of {largest} bytes pass and {largest + 1} bytes do not, so no link on the way carries more than {largest + PathMtu.HeaderBytes} bytes whole."
			: "Not even an empty packet got through. The host may not answer ICMP echo, or every packet is being dropped on the way.";

	private static Fact[] MtuFacts(PathMtuResult mtu) =>
	[
		new Fact("Largest passing payload", mtu.LargestPayload is { } largest ? $"{largest} bytes" : "none", mono: true),
		new Fact("Path MTU", mtu.PathMtuBytes is { } bytes ? $"{bytes} bytes" : "none", mono: true),
		new Fact("DF-set probes", mtu.Probes.ToString(CultureInfo.CurrentCulture)),
	];

	private async Task OnKeyAsync(KeyboardEventArgs args)
	{
		if (args.Key == "Enter" && !Busy)
		{
			await StartAsync();
		}
	}

	private Task PrimaryAsync()
	{
		if (Running)
		{
			Stop();
			return Task.CompletedTask;
		}

		return StartAsync();
	}

	private async Task StartAsync()
	{
		_error = null;
		if (!HostInput.TryParse(_host, out HostTarget? target, out string? problem))
		{
			_error = problem;
			return;
		}

		PingMode mode = _mode;
		int? count = _continuous ? 0 : ReadNumber(_count, 1, PingPlan.MaxCount);
		double? interval = ReadSeconds(_interval);
		int? timeout = ReadNumber(_timeout, NetworkSettings.MinPingTimeoutMilliseconds, NetworkSettings.MaxPingTimeoutMilliseconds);
		int? size = mode == PingMode.Icmp ? ReadNumber(_size, EchoRequest.MinPayload, EchoRequest.MaxPayload) : EchoRequest.DefaultPayload;
		int? port = mode == PingMode.Tcp ? ReadNumber(_port, NetworkSettings.MinTcpPingPort, NetworkSettings.MaxTcpPingPort) : Settings.TcpPingPort;
		_error = (count, interval, timeout, mode, size, port) switch
		{
			(null, _, _, _, _, _) => $"Count is a number from 1 to {PingPlan.MaxCount:N0}, or tick Until stopped.",
			(_, null, _, _, _, _) => $"Every is a number of seconds from {PingPlan.MinInterval.TotalSeconds:0.#} to {PingPlan.MaxInterval.TotalSeconds:0}.",
			(_, _, null, _, _, _) => $"Wait is from {NetworkSettings.MinPingTimeoutMilliseconds} to {NetworkSettings.MaxPingTimeoutMilliseconds:N0} ms.",
			(_, _, _, PingMode.Icmp, null, _) => $"Size is from {EchoRequest.MinPayload} to {EchoRequest.MaxPayload:N0} bytes.",
			(_, _, _, PingMode.Tcp, _, null) => $"Port is from {NetworkSettings.MinTcpPingPort} to {NetworkSettings.MaxTcpPingPort:N0}.",
			_ => null,
		};

		if (_error is not null)
		{
			return;
		}

		Recent.Remember(target.Host);
		AddressChoice choice = _choice;
		CancellationTokenSource run = BeginRun();
		_rows.Clear();
		_stats = new PingStatistics();
		_dropped = 0;
		_mtu = null;
		_target = $"Looking up {target.Host}";
		StateHasChanged();
		try
		{
			await Task.Run(
				async () =>
				{
					IPAddress address = await TargetResolver.ResolveAsync(target, choice, TargetResolver.DefaultTimeout, run.Token);
					Post(() => _target = Describe(target, address, mode, size!.Value, port!.Value));
					IPinger pinger = mode == PingMode.Tcp ? new TcpPinger(port!.Value) : SystemPinger.Instance;
					PingPlan plan = new(address, count!.Value, TimeSpan.FromSeconds(interval!.Value), new EchoRequest(EchoRequest.DefaultTtl, TimeSpan.FromMilliseconds(timeout!.Value), size!.Value));
					await PingRunner.RunAsync(pinger, plan, (sequence, reply) => Post(() => Add(sequence, reply)), Time, run.Token);
				},
				run.Token);
		}
		catch (ProbeException ex)
		{
			_error = ex.Message;
			_target = null;
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			// Stopped: the replies so far and their summary stay.
		}
		finally
		{
			EndRun(run);
		}
	}

	/// <summary>Path MTU discovery beside the run: DF-set IPv4 echo requests of halved sizes, so about eleven probes pin down the boundary.</summary>
	private async Task MtuAsync()
	{
		_error = null;
		if (!HostInput.TryParse(_host, out HostTarget? target, out string? problem))
		{
			_error = problem;
			return;
		}

		int? timeout = ReadNumber(_timeout, NetworkSettings.MinPingTimeoutMilliseconds, NetworkSettings.MaxPingTimeoutMilliseconds);
		if (timeout is null)
		{
			_error = $"Wait is from {NetworkSettings.MinPingTimeoutMilliseconds} to {NetworkSettings.MaxPingTimeoutMilliseconds:N0} ms.";
			return;
		}

		Recent.Remember(target.Host);
		AddressChoice choice = _choice;
		CancellationTokenSource run = BeginRun();
		_rows.Clear();
		_stats = new PingStatistics();
		_dropped = 0;
		_mtu = null;
		_target = $"Looking up {target.Host}";
		StateHasChanged();
		try
		{
			await Task.Run(
				async () =>
				{
					IPAddress address = await TargetResolver.ResolveAsync(target, choice, TargetResolver.DefaultTimeout, run.Token);
					if (address.AddressFamily != AddressFamily.InterNetwork)
					{
						throw new ProbeException("Path MTU discovery here covers IPv4 only, and this name has no IPv4 address to ask.");
					}

					Post(() => _target = $"Finding the path MTU to {address}");
					PathMtuResult result = await PathMtu.FindAsync(
						async (payload, token) => (await SystemPinger.Instance.SendAsync(
							address,
							new EchoRequest(EchoRequest.DefaultTtl, TimeSpan.FromMilliseconds(timeout.Value), payload, DontFragment: true),
							token)).Status == EchoStatus.Reply,
						run.Token);
					Post(() => _mtu = result);
				},
				run.Token);
		}
		catch (ProbeException ex)
		{
			_error = ex.Message;
			_target = null;
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			// Stopped: whatever the search found so far is not a boundary worth reporting.
		}
		finally
		{
			EndRun(run);
		}
	}

	private static string Describe(HostTarget target, IPAddress address, PingMode mode, int size, int port)
	{
		string how = mode == PingMode.Tcp ? $"over TCP port {port}" : $"with {size} bytes of data";
		return target.IsAddress
			? $"Pinging {address} {how}"
			: $"Pinging {target.Host} ({address}) {how}";
	}

	private static double? ReadSeconds(string text)
	{
		string trimmed = text.Trim();
		if (trimmed.Length == 0)
		{
			return 1;
		}

		bool read = double.TryParse(trimmed, NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out double seconds)
			|| double.TryParse(trimmed, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out seconds);
		return read && seconds >= PingPlan.MinInterval.TotalSeconds && seconds <= PingPlan.MaxInterval.TotalSeconds ? seconds : null;
	}

	private void Add(int sequence, EchoReply reply)
	{
		_stats.Add(reply);
		_rows.Add(new PingRow(sequence, reply));
		if (_rows.Count > MaxRows)
		{
			_rows.RemoveAt(0);
			_dropped++;
		}
	}
}
