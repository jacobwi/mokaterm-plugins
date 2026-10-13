using Moka.Passwords.Generation;
using Mokaterm.UI.Common.Components;

namespace Moka.Passwords.Components;

/// <summary>The one wording for each kind of value, shared by the tool's tabs, its messages and the settings page.</summary>
public static class ModeOptions
{
	public static IReadOnlyList<EnumOption<GenerationMode>> All { get; } =
	[
		new(GenerationMode.Password, "Password"),
		new(GenerationMode.Passphrase, "Passphrase"),
		new(GenerationMode.Pin, "PIN"),
	];

	/// <summary>The tool's tabs: the mode's name as the id, and its label.</summary>
	public static IReadOnlyList<(string Id, string Text)> Tabs { get; } = [.. All.Select(option => (option.Value.ToString(), option.Text))];

	public static string Name(GenerationMode mode) => All.First(option => option.Value == mode).Text;

	/// <summary>The name inside a sentence: "password", "passphrase", "PIN".</summary>
	public static string InSentence(GenerationMode mode) => mode switch
	{
		GenerationMode.Password => "password",
		GenerationMode.Passphrase => "passphrase",
		_ => "PIN",
	};
}
