using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Components.Web;
using Moka.Network.Components;
using Moka.Network.Echo;
using Moka.Network.Lookup;
using Moka.Network.Settings;
using Moka.Network.Targets;

namespace Moka.Network.Tool;

/// <summary>
/// Traces the path to a host hop by hop, drawing each hop as its probes come back. Names for the hops are looked up
/// beside the trace through the plugin's DNS client, so a slow PTR answer never holds the next hop up.
/// </summary>
public sealed partial class TraceSection : NetworkViewBase
{
	private readonly List<TraceHop> _hops = [];
	private readonly Dictionary<IPAddress, string> _names = [];
	private string _host = "";
	private string _maxHops = TraceOptions.MaxMaxHops.ToString(CultureInfo.InvariantCulture);
	private string _timeout = "";
	private bool _lookupNames = true;
	private AddressChoice _choice;
	private IPAddress? _destination;
	private TraceEnd? _end;
	private bool _stopped;
	private string? _target;
	private string? _error;

	private string Summary => Running
		? $"hop {_hops.Count}"
		: _end is TraceEnd.Reached ? $"{_hops.Count} hops" : "the routers on the way, hop by hop";

	protected override void OnInitialized() => _timeout = Math.Min(Settings.PingTimeoutMilliseconds, 3000).ToString(CultureInfo.CurrentCulture);

	private static string ProbeText(TraceProbe? probe) => probe switch
	{
		null => "",
		{ Milliseconds: { } milliseconds } => Milliseconds(milliseconds),
		{ Status: EchoStatus.TimedOut } => "*",
		{ Status: EchoStatus.Unreachable } => "!",
		_ => "?",
	};

	private static string? ProbeTitle(TraceProbe? probe) => probe switch
	{
		{ Status: EchoStatus.TimedOut } => "No answer in time",
		{ Status: EchoStatus.Unreachable } unreachable => $"Unreachable: {unreachable.Detail}",
		{ Status: EchoStatus.Failed } failed => $"Failed: {failed.Detail}",
		_ => null,
	};

	private static string ProbeClass(TraceProbe? probe) => probe?.Status switch
	{
		EchoStatus.TimedOut => "mn-td mn-td--mono mn-td--end mn-td--dim mn-c-probe",
		EchoStatus.Unreachable or EchoStatus.Failed => "mn-td mn-td--mono mn-td--end mn-td--bad mn-c-probe",
		_ => "mn-td mn-td--mono mn-td--end mn-c-probe",
	};

	private string EndClass(TraceEnd end) => _stopped
		? "mn-result-line"
		: end == TraceEnd.Reached ? "mn-result-line mn-result-line--good" : "mn-result-line mn-result-line--bad";

	private string EndText(TraceEnd end)
	{
		if (_stopped)
		{
			return $"Stopped at hop {_hops.Count}.";
		}

		return end switch
		{
			TraceEnd.Reached => $"Reached {_destination} in {_hops.Count} hops.",
			TraceEnd.Unreachable when _hops.LastOrDefault()?.Unreachable is { } probe =>
				$"{(probe.From is { } from ? from.ToString() : "A router")} says {_destination} cannot be reached: {probe.Detail}.",
			TraceEnd.Unreachable => $"{_destination} cannot be reached.",
			_ => $"Did not reach {_destination} within {_hops.Count} hops. It may not answer ICMP, or the path is longer.",
		};
	}

	private async Task OnKeyAsync(KeyboardEventArgs args)
	{
		if (args.Key == "Enter" && !Busy)
		{
			await TraceAsync();
		}
	}

	private Task PrimaryAsync()
	{
		if (Running)
		{
			Stop();
			return Task.CompletedTask;
		}

		return TraceAsync();
	}

	private async Task TraceAsync()
	{
		_error = null;
		if (!HostInput.TryParse(_host, out HostTarget? target, out string? problem))
		{
			_error = problem;
			return;
		}

		int? maxHops = ReadNumber(_maxHops, 1, TraceOptions.MaxMaxHops);
		int? timeout = ReadNumber(_timeout, NetworkSettings.MinPingTimeoutMilliseconds, NetworkSettings.MaxPingTimeoutMilliseconds);
		if (maxHops is null)
		{
			_error = $"Max hops is from 1 to {TraceOptions.MaxMaxHops}.";
			return;
		}

		if (timeout is null)
		{
			_error = $"Wait is from {NetworkSettings.MinPingTimeoutMilliseconds} to {NetworkSettings.MaxPingTimeoutMilliseconds} ms.";
			return;
		}

		NetworkSettings settings = Settings;
		AddressChoice choice = _choice;
		bool lookupNames = _lookupNames;
		CancellationTokenSource run = BeginRun();
		_hops.Clear();
		_names.Clear();
		_end = null;
		_stopped = false;
		_destination = null;
		_target = $"Looking up {target.Host}";
		StateHasChanged();
		try
		{
			TraceEnd end = await Task.Run(
				async () =>
				{
					IPAddress address = await TargetResolver.ResolveAsync(target, choice, TargetResolver.DefaultTimeout, run.Token);
					Post(() =>
					{
						_destination = address;
						_target = target.IsAddress
							? $"Tracing {address}, at most {maxHops} hops"
							: $"Tracing {target.Host} ({address}), at most {maxHops} hops";
					});

					ReverseNames? names = null;
					if (lookupNames)
					{
						IReadOnlyList<IPEndPoint> servers = DnsResolvers.Servers(settings.Resolver, settings.CustomResolver, out _);
						if (servers.Count > 0)
						{
							names = new ReverseNames(
								(hop, token) => DnsLookup.ReverseAsync(servers, hop, settings.DnsTimeout, token),
								(hop, name) => Post(() => _names[hop] = name),
								run.Token);
						}
					}

					TraceOptions options = new(maxHops.Value, TraceOptions.DefaultProbesPerHop, TimeSpan.FromMilliseconds(timeout.Value));
					return await Traceroute.RunAsync(SystemPinger.Instance, address, options, hop =>
					{
						names?.Request(hop.Addresses);
						Post(() => Show(hop));
					}, run.Token);
				},
				run.Token);
			_end = end;
		}
		catch (ProbeException ex)
		{
			_error = ex.Message;
			_target = null;
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			_stopped = true;
			_end = TraceEnd.OutOfHops;
		}
		finally
		{
			EndRun(run);
		}
	}

	private void Show(TraceHop hop)
	{
		int index = _hops.FindIndex(existing => existing.Ttl == hop.Ttl);
		if (index >= 0)
		{
			_hops[index] = hop;
		}
		else
		{
			_hops.Add(hop);
		}
	}
}
