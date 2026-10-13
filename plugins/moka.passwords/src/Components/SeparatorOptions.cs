using Moka.Passwords.Generation;
using Mokaterm.UI.Common.Components;

namespace Moka.Passwords.Components;

/// <summary>
/// The separator choices, shared by the tool and the settings page. Named in words: a lone "." is too small to read, and a
/// segment clips an underscore.
/// </summary>
public static class SeparatorOptions
{
	public static IReadOnlyList<EnumOption<PassphraseSeparator>> All { get; } =
	[
		new(PassphraseSeparator.Space, "Space"),
		new(PassphraseSeparator.Dash, "Dash"),
		new(PassphraseSeparator.Dot, "Dot"),
		new(PassphraseSeparator.Underscore, "Underscore"),
		new(PassphraseSeparator.None, "None"),
		new(PassphraseSeparator.Custom, "Custom"),
	];
}
