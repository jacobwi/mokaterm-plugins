using Moka.Passwords.Generation;
using Mokaterm.UI.Common.Components;

namespace Moka.Passwords.Settings;

/// <summary>The plugin's settings page, in the Plugins group, built from the shared settings fields.</summary>
public sealed partial class PasswordSettingsPage : SettingsSectionBase<PasswordSettings>
{
	private Task SaveAsync(Func<PasswordSettings, PasswordSettings> change) => UpdateAsync(settings => change(settings).Clamped());

	/// <summary>The credit for both lists the tool can draw from, which the license asks for.</summary>
	private static string Credit => $"{WordList.Large.Credit}; {WordList.Small.Credit}";

	private Task SaveKindsAsync(PasswordOptions options) => SaveAsync(settings => settings with
	{
		Lowercase = options.Lowercase,
		Uppercase = options.Uppercase,
		Digits = options.Digits,
		Symbols = options.Symbols,
	});
}
