using System.Net;
using Microsoft.AspNetCore.Components.Web;
using Moka.Network.Components;
using Moka.Network.Ports;
using Moka.Network.Targets;
using Moka.Red.Core.Enums;
using Mokaterm.Abstractions.Protocols;

namespace Moka.Network.Tool;

/// <summary>
/// Checks whether a few TCP ports on one host accept a connection: all at once, each with its own time limit, each
/// row filled in as its answer comes. A known list rather than a range keeps it a check, not a scan.
/// </summary>
public sealed partial class PortSection : NetworkViewBase
{
	private readonly List<PortRow> _rows = [];
	private string _host = "";
	private string _ports = "22";
	private bool _greeting = true;
	private bool _greetingAsked;
	private AddressChoice _choice;
	private string? _target;
	private string? _error;

	private string Summary => Running
		? "connecting"
		: _rows.Count > 0 ? $"{_rows.Count(row => row.Result?.State == PortState.Open)} of {_rows.Count} open" : "does a TCP port accept a connection";

	private static string StateText(PortState state) => state switch
	{
		PortState.Open => "open",
		PortState.Refused => "refused",
		PortState.TimedOut => "no answer",
		PortState.Unreachable => "unreachable",
		_ => "failed",
	};

	private static MokaColor StateColor(PortState state) => state switch
	{
		PortState.Open => MokaColor.Success,
		PortState.Refused => MokaColor.Error,
		PortState.TimedOut => MokaColor.Warning,
		_ => MokaColor.Error,
	};

	private static string Endpoint(PortResult result) => HostEndpoint.Format(result.Endpoint.Address.ToString(), result.Endpoint.Port);

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
		_error = null;
		if (!HostInput.TryParse(_host, out HostTarget? target, out string? problem) || !PortList.TryParse(_ports, out IReadOnlyList<int>? ports, out problem))
		{
			_error = problem;
			return;
		}

		AddressChoice choice = _choice;
		bool greeting = _greeting;
		TimeSpan timeout = Settings.ConnectTimeout;
		CancellationTokenSource run = BeginRun();
		_rows.Clear();
		_rows.AddRange(ports.Select(port => new PortRow(port)));
		_greetingAsked = greeting;
		_target = $"Looking up {target.Host}";
		StateHasChanged();
		try
		{
			await Task.Run(
				async () =>
				{
					IPAddress address = await TargetResolver.ResolveAsync(target, choice, TargetResolver.DefaultTimeout, run.Token);
					Post(() => _target = target.IsAddress ? $"Connecting to {address}" : $"Connecting to {target.Host} at {address}");
					await Task.WhenAll(ports.Select(async port =>
					{
						PortResult result = await PortCheck.CheckAsync(address, port, timeout, greeting, run.Token);
						Post(() => SetResult(result));
					}));
				},
				run.Token);
		}
		catch (ProbeException ex)
		{
			_error = ex.Message;
			_target = null;
			_rows.Clear();
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			_rows.RemoveAll(row => row.Result is null);
		}
		finally
		{
			EndRun(run);
		}
	}

	private void SetResult(PortResult result)
	{
		int index = _rows.FindIndex(row => row.Port == result.Port);
		if (index >= 0)
		{
			_rows[index] = _rows[index] with { Result = result };
		}
	}
}
