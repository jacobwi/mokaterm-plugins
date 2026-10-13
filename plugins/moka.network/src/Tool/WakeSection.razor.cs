using System.Net;
using Microsoft.AspNetCore.Components.Web;
using Moka.Network.Components;
using Moka.Network.Targets;
using Moka.Network.Wake;
using Mokaterm.Abstractions.Protocols;
using Mokaterm.UI.Common.Components;

namespace Moka.Network.Tool;

/// <summary>
/// Sends a Wake-on-LAN magic packet and says exactly what went where. Nothing is kept: the MAC and the address live in
/// the fields while the tool is open and are gone when it closes.
/// </summary>
public sealed partial class WakeSection : NetworkViewBase
{
	private static readonly IReadOnlyList<EnumOption<WakePort>> Ports =
	[
		new(WakePort.Discard, "9"),
		new(WakePort.Echo, "7"),
	];

	private string _mac = "";
	private string _target = "";
	private WakePort _port = WakePort.Discard;
	private WakeSent? _sent;
	private string? _error;

	private string Summary => _sent is { } sent ? $"last sent to {sent.Endpoint}" : "a magic packet to wake a machine";

	private async Task OnKeyAsync(KeyboardEventArgs args)
	{
		if (args.Key == "Enter" && !Busy)
		{
			await SendAsync();
		}
	}

	private async Task SendAsync()
	{
		_error = null;
		if (!MacAddress.TryParse(_mac, out MacAddress? mac, out string? problem) || !WakeTarget.TryParse(_target, out IPAddress? address, out problem))
		{
			_error = problem;
			return;
		}

		byte[] packet = MagicPacket.Build(mac);
		IPEndPoint endpoint = new(address, (int)_port);
		CancellationTokenSource run = BeginRun();
		try
		{
			int bytes = await Task.Run(() => WakeSender.SendAsync(packet, endpoint, run.Token), run.Token);
			_sent = new WakeSent(mac.ToString(), HostEndpoint.Format(address.ToString(), endpoint.Port), bytes, MagicPacket.Hex(packet), Time.GetLocalNow());
		}
		catch (ProbeException ex)
		{
			_error = ex.Message;
			_sent = null;
		}
		catch (OperationCanceledException) when (run.IsCancellationRequested)
		{
			// The tool closed while sending.
		}
		finally
		{
			EndRun(run);
		}
	}
}
