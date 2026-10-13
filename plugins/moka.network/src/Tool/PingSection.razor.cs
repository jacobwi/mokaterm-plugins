using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Components.Web;
using Moka.Network.Components;
using Moka.Network.Echo;
using Moka.Network.Settings;
using Moka.Network.Targets;

namespace Moka.Network.Tool;

/// <summary>
/// Pings a host a set number of times or until stopped, one request per interval, with a line per reply and the
/// summary kept up to date as replies arrive. The list keeps the newest <see cref="MaxRows"/>; the summary counts all.
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
	private bool _continuous;
	private AddressChoice _choice;
	private PingStatistics _stats = new();
	private int _dropped;
	private string? _target;
	private string? _error;

	private string Summary => Running
		? $"{_stats.Sent} sent"
		: _stats.Sent > 0 ? $"{_stats.Received} of {_stats.Sent} answered" : "ICMP echo, one request at a time";

	private string LossText => _stats.LossPercent.ToString(_stats.LossPercent is 0 or 100 ? "0" : "0.#", CultureInfo.CurrentCulture) + "%";

	protected override void OnInitialized() => _timeout = Settings.PingTimeoutMilliseconds.ToString(CultureInfo.CurrentCulture);

	private static string Rtt(double? milliseconds) => milliseconds is { } value ? Milliseconds(value) : "-";

	private static string ResultText(EchoReply reply) => reply.Status switch
	{
		EchoStatus.Reply => "reply",
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

		int? count = _continuous ? 0 : ReadNumber(_count, 1, PingPlan.MaxCount);
		double? interval = ReadSeconds(_interval);
		int? timeout = ReadNumber(_timeout, NetworkSettings.MinPingTimeoutMilliseconds, NetworkSettings.MaxPingTimeoutMilliseconds);
		int? size = ReadNumber(_size, EchoRequest.MinPayload, EchoRequest.MaxPayload);
		_error = (count, interval, timeout, size) switch
		{
			(null, _, _, _) => $"Count is a number from 1 to {PingPlan.MaxCount:N0}, or tick Until stopped.",
			(_, null, _, _) => $"Every is a number of seconds from {PingPlan.MinInterval.TotalSeconds:0.#} to {PingPlan.MaxInterval.TotalSeconds:0}.",
			(_, _, null, _) => $"Wait is from {NetworkSettings.MinPingTimeoutMilliseconds} to {NetworkSettings.MaxPingTimeoutMilliseconds:N0} ms.",
			(_, _, _, null) => $"Size is from {EchoRequest.MinPayload} to {EchoRequest.MaxPayload:N0} bytes.",
			_ => null,
		};

		if (_error is not null)
		{
			return;
		}

		AddressChoice choice = _choice;
		CancellationTokenSource run = BeginRun();
		_rows.Clear();
		_stats = new PingStatistics();
		_dropped = 0;
		_target = $"Looking up {target.Host}";
		StateHasChanged();
		try
		{
			await Task.Run(
				async () =>
				{
					IPAddress address = await TargetResolver.ResolveAsync(target, choice, TargetResolver.DefaultTimeout, run.Token);
					Post(() => _target = Describe(target, address, size!.Value));
					PingPlan plan = new(address, count!.Value, TimeSpan.FromSeconds(interval!.Value), new EchoRequest(EchoRequest.DefaultTtl, TimeSpan.FromMilliseconds(timeout!.Value), size!.Value));
					await PingRunner.RunAsync(SystemPinger.Instance, plan, (sequence, reply) => Post(() => Add(sequence, reply)), Time, run.Token);
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

	private static string Describe(HostTarget target, IPAddress address, int size) => target.IsAddress
		? $"Pinging {address} with {size} bytes"
		: $"Pinging {target.Host} ({address}) with {size} bytes";

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
