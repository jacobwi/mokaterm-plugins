namespace Moka.Network.Echo;

/// <summary>How the ping tab sends its requests: ICMP echo, or a TCP connection attempt for a host that drops ICMP.</summary>
public enum PingMode
{
	/// <summary>The system's ICMP echo, through <see cref="SystemPinger"/>. The default.</summary>
	Icmp,

	/// <summary>One TCP connection attempt per request, through <see cref="TcpPinger"/>.</summary>
	Tcp,
}
