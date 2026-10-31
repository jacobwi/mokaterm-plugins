using System.Net;
using System.Net.NetworkInformation;

namespace Moka.Network;

/// <summary>One connection this machine holds with another, in the words of <see cref="LocalConnections.Describe"/>.</summary>
public sealed record LocalConnectionRow(string Local, string Remote, string State);

/// <summary>One port this machine listens on, tcp or udp, with no process named: see <see cref="LocalConnections"/>.</summary>
public sealed record LocalListenerRow(string Protocol, string Local);

/// <summary>A whole read of this machine's connection table.</summary>
public sealed record LocalTable(IReadOnlyList<LocalConnectionRow> Connections, IReadOnlyList<LocalListenerRow> Listeners);

/// <summary>
/// This machine's own connection table, read through .NET's <see cref="IPGlobalProperties"/>: the TCP connections it
/// holds, and the ports it listens on over TCP and UDP. The mapping to rows and the state names in words are plain
/// functions the tests drive with shaped data; <see cref="Collect"/> is the thin live read the desktop panel draws.
/// The .NET APIs say nothing about which process owns a connection, so the table cannot either, and the panel says so
/// rather than guessing.
/// </summary>
public static class LocalConnections
{
	public static string Describe(TcpState state) => state switch
	{
		TcpState.Closed => "Closed",
		TcpState.Listen => "Listening",
		TcpState.SynSent => "Opening",
		TcpState.SynReceived => "Opening",
		TcpState.Established => "Established",
		TcpState.FinWait1 => "Closing",
		TcpState.FinWait2 => "Closing, other end done",
		TcpState.CloseWait => "Closing, this end done",
		TcpState.Closing => "Closing",
		TcpState.LastAck => "Closing, last ack",
		TcpState.TimeWait => "Waiting out the close",
		TcpState.DeleteTcb => "Deleting",
		_ => "Unknown",
	};

	/// <summary>The end as one string, which <see cref="IPEndPoint"/> already writes well: IPv6 in brackets, the port after a colon.</summary>
	public static string Show(IPEndPoint? endpoint) => endpoint?.ToString() ?? "";

	public static IReadOnlyList<LocalConnectionRow> Connections(IEnumerable<TcpConnectionInformation> connections) =>
		[.. connections
			.Select(connection => new LocalConnectionRow(Show(connection.LocalEndPoint), Show(connection.RemoteEndPoint), Describe(connection.State)))
			.OrderBy(row => row.State, StringComparer.CurrentCulture)
			.ThenBy(row => row.Local, StringComparer.CurrentCulture)];

	public static IReadOnlyList<LocalListenerRow> Listeners(IEnumerable<IPEndPoint> tcp, IEnumerable<IPEndPoint> udp) =>
	[
		.. tcp.Select(endPoint => new LocalListenerRow("tcp", Show(endPoint)))
			.OrderBy(row => row.Local, StringComparer.CurrentCulture),
		.. udp.Select(endPoint => new LocalListenerRow("udp", Show(endPoint)))
			.OrderBy(row => row.Local, StringComparer.CurrentCulture),
	];

	/// <summary>Reads the table now, on the machine this runs on. IPv6 ends come out with brackets and the port after a colon.</summary>
	public static LocalTable Collect()
	{
		IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();
		return new LocalTable(
			Connections(properties.GetActiveTcpConnections()),
			Listeners(properties.GetActiveTcpListeners(), properties.GetActiveUdpListeners()));
	}
}
