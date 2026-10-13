using Moka.Red.Core.Icons;

namespace Moka.Passwords.Components;

/// <summary>The plugin's icon, in Mokaterm's style: one stroked path on a 24 by 24 grid.</summary>
public static class PasswordIcons
{
	/// <summary>A password field: a box, two asterisks and a caret.</summary>
	public static readonly MokaIconDefinition Generator = new("mp-generator",
		"M3 7h18v10H3z M7.5 10v4 M5.8 11l3.4 2 M5.8 13l3.4-2 M13 10v4 M11.3 11l3.4 2 M11.3 13l3.4-2 M18 10v4");
}
