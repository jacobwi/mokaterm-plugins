using System.Globalization;
using Moka.Network.Targets;

namespace Moka.Network.Server;

/// <summary>The link-layer protocols the capture tab offers: tcp, udp and icmp.</summary>
public enum CaptureProtocol
{
	Tcp,

	Udp,

	Icmp,
}

/// <summary>
/// The capture tab's filter: a host, a port and one of three protocols, each checked here and only here. There is
/// deliberately no free-text expression field: a tcpdump filter is a small language, and a wrong one can dump
/// traffic nobody meant to see, so the tool builds the expression itself out of the three fields and refuses to pass
/// anything else through.
/// </summary>
public sealed record TcpDumpFilter
{
	public CaptureProtocol Protocol { get; init; } = CaptureProtocol.Tcp;

	/// <summary>The host to follow, or null for any host.</summary>
	public HostTarget? Host { get; init; }

	/// <summary>The port to follow, or null for any port.</summary>
	public int? Port { get; init; }

	/// <summary>The filter as tcpdump reads it, built word by word out of what was checked, never out of typed text.</summary>
	public string Expression
	{
		get
		{
			List<string> parts = [ProtocolWord(Protocol)];
			if (Host is { } host)
			{
				parts.Add("host " + host.Host);
			}

			if (Port is { } port)
			{
				parts.Add("port " + port.ToString(CultureInfo.InvariantCulture));
			}

			return string.Join(" and ", parts);
		}
	}

	public static string ProtocolWord(CaptureProtocol protocol) => protocol switch
	{
		CaptureProtocol.Tcp => "tcp",
		CaptureProtocol.Udp => "udp",
		CaptureProtocol.Icmp => "icmp",
		_ => throw new ArgumentOutOfRangeException(nameof(protocol), "The protocol must be tcp, udp or icmp."),
	};

	/// <summary>
	/// Reads the three fields: each may be empty, and each must then be a host as the host boxes read it, a port from
	/// 1 to 65535, and one of the three protocols, with icmp refusing a port because icmp has none.
	/// </summary>
	/// <returns>The filter, or null with <paramref name="problem"/> worded for the user when a field does not read.</returns>
	public static TcpDumpFilter? TryRead(string? hostText, string? portText, CaptureProtocol protocol, out string? problem)
	{
		if (!Enum.IsDefined(protocol))
		{
			problem = "Pick tcp, udp or icmp.";
			return null;
		}

		HostTarget? host = null;
		string hostInput = (hostText ?? "").Trim();
		if (hostInput.Length > 0 && !HostInput.TryParse(hostInput, out host, out problem))
		{
			return null;
		}

		string portInput = (portText ?? "").Trim();
		int? port = null;
		if (portInput.Length > 0)
		{
			if (!int.TryParse(portInput, NumberStyles.None, CultureInfo.CurrentCulture, out int read) || read is < 1 or > 65535)
			{
				problem = "Port is a number from 1 to 65535, or empty for any port.";
				return null;
			}

			if (protocol == CaptureProtocol.Icmp)
			{
				problem = "icmp has no ports: clear the port, or pick tcp or udp.";
				return null;
			}

			port = read;
		}

		problem = null;
		return new TcpDumpFilter { Protocol = protocol, Host = host, Port = port };
	}
}
