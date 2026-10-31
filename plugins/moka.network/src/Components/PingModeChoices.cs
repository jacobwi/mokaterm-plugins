using Moka.Network.Echo;
using Mokaterm.UI.Common.Components;

namespace Moka.Network.Components;

/// <summary>The request kind picker for the ping tab: ICMP echo, or TCP connection attempts.</summary>
public static class PingModeChoices
{
	public static readonly IReadOnlyList<EnumOption<PingMode>> Options =
	[
		new(PingMode.Icmp, "ICMP"),
		new(PingMode.Tcp, "TCP"),
	];
}
