using Moka.Red.Core.Icons;
using Moka.Red.Icons;

namespace Moka.DevTools.Components;

/// <summary>The icons the plugin draws beyond Moka.Red's, in the same style: stroked paths on a 24 by 24 grid.</summary>
public static class DevToolIcons
{
	/// <summary>Angle brackets around a slash, the tool itself.</summary>
	public static readonly MokaIconDefinition Tool = new("dt-tool", "M8 7l-5 5 5 5 M16 7l5 5-5 5 M14 4l-4 16");

	/// <summary>A token: a card with two lines of claims.</summary>
	public static readonly MokaIconDefinition Jwt = new("dt-jwt", "M3 6h18v12H3z M7 10h10 M7 14h6");

	public static readonly MokaIconDefinition Encode = new("dt-encode", "M4 8h13l-3-3 M20 16H7l3 3");

	public static readonly MokaIconDefinition Hash = new("dt-hash", "M9 4L7 20 M17 4l-2 16 M4 9h17 M3 15h17");

	/// <summary>An identity card.</summary>
	public static readonly MokaIconDefinition Ids = new("dt-ids", "M3 6h18v12H3z M7 10h4 M7 14h6 M15 10h2 M15 14h2");

	public static readonly MokaIconDefinition Time = MokaIcons.Status.Clock;

	/// <summary>A pair of braces.</summary>
	public static readonly MokaIconDefinition Data = new("dt-data",
		"M9 4H8a2 2 0 0 0-2 2v4a2 2 0 0 1-2 2 2 2 0 0 1 2 2v4a2 2 0 0 0 2 2h1 M15 4h1a2 2 0 0 1 2 2v4a2 2 0 0 0 2 2 2 2 0 0 0-2 2v4a2 2 0 0 1-2 2h-1");

	/// <summary>A dot and a star: .* in a pattern.</summary>
	public static readonly MokaIconDefinition Regex = new("dt-regex", "M6 18h.01 M15 4v8 M11.5 6l7 4 M11.5 10l7-4");

	public static readonly MokaIconDefinition Swap = new("dt-swap", "M7 4L3 8l4 4 M3 8h14 M17 12l4 4-4 4 M21 16H7");

	public static readonly MokaIconDefinition Run = new("dt-run", "M7 5l12 7-12 7z");
}
