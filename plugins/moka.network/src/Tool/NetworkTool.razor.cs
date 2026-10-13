using Moka.Network.Components;
using Mokaterm.UI.Common.ToolPages;

namespace Moka.Network.Tool;

/// <summary>
/// The Network Toolkit app tool. It needs no session: every probe starts on the machine Mokaterm runs on, which each
/// tab says, so on the web host a result describes the server's view of the network rather than the browser's.
/// </summary>
public sealed partial class NetworkTool : AppToolBase
{
	private const string DnsId = "dns";
	private const string PingId = "ping";
	private const string PortId = "port";
	private const string TraceId = "trace";
	private const string WhoisId = "whois";
	private const string WakeId = "wake";
	private const string SubnetId = "subnet";

	private static readonly IReadOnlyList<ToolNavEntry> Entries =
	[
		new(DnsId, "DNS lookup", NetworkIcons.Dns),
		new(PingId, "Ping", NetworkIcons.Ping),
		new(PortId, "Port check", NetworkIcons.Port),
		new(TraceId, "Traceroute", NetworkIcons.Trace),
		new(WhoisId, "Whois", NetworkIcons.Whois),
		new(WakeId, "Wake-on-LAN", NetworkIcons.Wake),
		new(SubnetId, "Subnets", NetworkIcons.Subnet),
	];

	private string _current = DnsId;

	private void Show(string id) => _current = id;
}
