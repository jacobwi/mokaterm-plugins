using Microsoft.Extensions.DependencyInjection;
using Moka.Passwords;
using Moka.Passwords.Components;
using Moka.Passwords.Settings;
using Moka.Passwords.Tool;
using Mokaterm.Abstractions.Modules;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Plugins;
using Mokaterm.UI.Common.Contributions;
using Mokaterm.UI.Common.Icons;

[assembly: MokatermPlugin(typeof(PasswordsModule))]

namespace Moka.Passwords;

/// <summary>What the Password Generator plugin adds: the app tool and its settings page.</summary>
public sealed class PasswordsModule : IMokatermModule
{
	/// <summary>The app tool's id, which names its palette command and any shortcut bound to it.</summary>
	public const string ToolId = "password-generator";

	public ModuleInfo Info { get; } = new(
		"moka.passwords",
		"Password Generator",
		"Generate passwords, passphrases and PINs, see how strong their settings are, and copy them so the clipboard clears.",
		DisplayVersion.Of(typeof(PasswordsModule).Assembly));

	public void ConfigureServices(IServiceCollection services)
	{
		services.AddAppTool(new AppToolDescriptor
		{
			Id = ToolId,
			Title = "Password Generator",
			Icon = PasswordIcons.Generator,
			Description = "Generate passwords, passphrases and PINs, and copy them so the clipboard clears",
			Component = typeof(PasswordGeneratorTool),
			Size = SessionToolSize.Standard,
		});

		// The page id, like the settings key, is plugin. and the plugin's id: the loader refuses a plugin that uses any other.
		services.AddSettingsPage(new SettingsPageDescriptor
		{
			Id = PasswordSettings.SectionKey,
			Title = "Password Generator",
			Description = "What the Password Generator starts with: length, characters, words, separators, PIN digits and how many to make.",
			Group = SettingsPageGroup.Plugins,
			Icon = MokatermIcons.Plugin,
			Component = typeof(PasswordSettingsPage),
			Keywords = "password passphrase pin generator random symbols length words diceware eff look-alike",
		});
	}
}
