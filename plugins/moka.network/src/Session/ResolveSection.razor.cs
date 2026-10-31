using System.Net;
using Microsoft.AspNetCore.Components.Web;
using Moka.Network.Server;
using Moka.Network.Targets;

namespace Moka.Network.Session;

/// <summary>
/// Names as the server's own resolver sees them: <c>getent hosts</c> for a name and for an address, plus the
/// nameservers out of the server's <c>/etc/resolv.conf</c>, which together say why a server answers differently than
/// the desktop would. The tab probes for getent once it lands on a connected session and says so plainly when the
/// server does not have it.
/// </summary>
public sealed partial class ResolveSection : SessionSectionBase
{
	private string _host = "";
	private string? _error;
	private string? _target;
	private IReadOnlyList<GetentHost> _rows = [];
	private IReadOnlyList<IPAddress> _nameservers = [];
	private bool _probed;
	private bool _hasGetent;
	private bool _reverse;

	private bool Offline => Session is not null && !Connected;

	private bool _absent => _probed && !_hasGetent;

	private bool NotReady => !Connected || _absent;

	private string Summary => _probed
		? _hasGetent ? "getent answers for this server" : "no getent on this server"
		: "Asking the server what it has";

	private string PrimaryText => Running ? "Stop" : "Look up";

	private string? PrimaryTitle => !Connected
		? "The session is not connected."
		: _probed && !_hasGetent ? "This server has no getent."
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
		IReadOnlySet<string> present = await runner.ProgramsAsync(["getent", "cat"], Settings.ServerTimeout);
		Post(() =>
		{
			_hasGetent = present.Contains("getent");
			if (_hasGetent)
			{
				// The resolvers the server itself asks, read the one way every Linux server offers.
				_ = ReadResolversAsync(runner);
			}
		});
	}

	private async Task ReadResolversAsync(ServerRunner runner)
	{
		ServerRun read = await runner.RunAsync(ServerQuote.Line(["cat", "/etc/resolv.conf"]), Settings.ServerTimeout);
		if (!read.Ok)
		{
			// Not worth an error of its own: the lookup still works, and the tab simply does not list resolvers.
			return;
		}

		IReadOnlyList<IPAddress> servers = ResolvConf.Nameservers(read.Output);
		Post(() => _nameservers = servers);
	}

	private Task PrimaryAsync()
	{
		if (Running)
		{
			Stop();
			return Task.CompletedTask;
		}

		return LookUpAsync();
	}

	private async Task OnKeyAsync(KeyboardEventArgs args)
	{
		if (args.Key == "Enter" && !Busy)
		{
			await LookUpAsync();
		}
	}

	private async Task LookUpAsync()
	{
		_error = null;
		if (Runner is not { } runner)
		{
			return;
		}

		if (_probed && !_hasGetent)
		{
			_error = "This server has no getent, so there is nothing to ask here.";
			return;
		}

		if (!HostInput.TryParse(_host, out HostTarget? target, out string? problem))
		{
			_error = problem;
			return;
		}

		_reverse = target.IsAddress;
		_target = _reverse ? $"Asking the server for the name of {target.Host}" : $"Asking the server for {target.Host}";
		_rows = [];
		CancellationTokenSource run = BeginRun();
		StateHasChanged();
		try
		{
			string command = ServerQuote.Line(["getent", "hosts", target.Host]);
			ServerRun answer = await runner.RunAsync(command, Settings.ServerTimeout);
			Post(() =>
			{
				if (answer.ExitCode is 2)
				{
					// getent's own word for a miss, which is an answer, not a failure.
					_rows = [];
					_target = _reverse ? $"The server has no name for {target.Host}" : $"The server found no address for {target.Host}";
					return;
				}

				if (answer.Ok)
				{
					_rows = GetentHosts.Parse(answer.Output);
					_target = _reverse
						? _rows.Count > 0 ? $"The name of {target.Host} on the server" : $"The server has no name for {target.Host}"
						: _rows.Count > 0 ? $"{target.Host} on the server" : $"The server found no address for {target.Host}";
					return;
				}

				_rows = [];
				_error = answer.NeverRan || answer.TimedOut || answer.Broke
					? answer.Problem
					: $"getent ended with status {answer.ExitCode}.";
			});
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			// Stopped: nothing more to say.
		}
		finally
		{
			EndRun(run);
		}
	}

	private static string NameOf(GetentHost host) => host.Aliases.Count > 0 ? string.Join(' ', [host.Name, .. host.Aliases]) : host.Name;
}
