using System.Net;
using System.Net.Sockets;
using Moka.Network.Targets;
using Mokaterm.Abstractions.Protocols;

namespace Moka.Network.Wake;

/// <summary>Sends one magic packet over UDP. Nothing comes back from a sleeping machine, so sending is all there is to check.</summary>
public static class WakeSender
{
	public static async Task<int> SendAsync(byte[] packet, IPEndPoint target, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(packet);
		ArgumentNullException.ThrowIfNull(target);
		using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true };
		try
		{
			return await socket.SendToAsync(packet, SocketFlags.None, target, cancellationToken);
		}
		catch (SocketException ex)
		{
			string endpoint = HostEndpoint.Format(target.Address.ToString(), target.Port);
			throw new ProbeException(ex.SocketErrorCode switch
			{
				SocketError.AccessDenied => $"This machine refused to send a broadcast to {endpoint}.",
				SocketError.NetworkUnreachable or SocketError.HostUnreachable => $"This machine has no route to {endpoint}.",
				_ => $"Sending to {endpoint} failed ({ex.SocketErrorCode}).",
			}, ex);
		}
	}
}
