using Moka.Red.Core.Icons;
using Mokaterm.UI.Common.Icons;

namespace Moka.Network.Components;

/// <summary>The icons the plugin draws beyond Mokaterm's, in the same style: stroked paths on a 24 by 24 grid.</summary>
public static class NetworkIcons
{
	/// <summary>Three linked machines, the tool itself.</summary>
	public static readonly MokaIconDefinition Network = new("mn-network",
		"M9 3h6v5H9z M3 16h6v5H3z M15 16h6v5h-6z M12 8v4 M6 16v-4h12v4");

	public static readonly MokaIconDefinition Dns = MokatermIcons.Globe;

	/// <summary>A pulse line.</summary>
	public static readonly MokaIconDefinition Ping = new("mn-ping",
		"M2 12h4l3-8 4 16 3-8h6");

	/// <summary>A plug.</summary>
	public static readonly MokaIconDefinition Port = new("mn-port",
		"M9 2v5 M15 2v5 M6 7h12v4a6 6 0 0 1-12 0z M12 17v5");

	/// <summary>A route between two points.</summary>
	public static readonly MokaIconDefinition Trace = new("mn-trace",
		"M6 19a2 2 0 1 1-4 0 2 2 0 0 1 4 0z M22 5a2 2 0 1 1-4 0 2 2 0 0 1 4 0z M6 19h7a4 4 0 0 0 0-8h-2a4 4 0 0 1 0-8h7");

	/// <summary>A card with lines, a registry record.</summary>
	public static readonly MokaIconDefinition Whois = new("mn-whois",
		"M3 5h18v14H3z M7 9h6 M7 13h10 M7 16h4");

	public static readonly MokaIconDefinition Wake = MokatermIcons.Power;

	/// <summary>A grid split into blocks.</summary>
	public static readonly MokaIconDefinition Subnet = new("mn-subnet",
		"M3 3h18v18H3z M3 12h18 M12 3v18 M12 7.5h9");

	public static readonly MokaIconDefinition Start = MokatermIcons.Play;

	/// <summary>A filled square drawn as a stroke.</summary>
	public static readonly MokaIconDefinition Stop = new("mn-stop",
		"M6 6h12v12H6z");

	public static readonly MokaIconDefinition Send = MokatermIcons.Broadcast;
}
