using Microsoft.Extensions.DependencyInjection;
using Moka.Passwords.Generation;
using Moka.Passwords.Settings;
using Moka.Passwords.Tool;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Plugin.TestHost;

namespace Moka.Passwords.Tests;

/// <summary>The generator tool renders on both hosts: the three mode tabs and the options of the mode it opens on.</summary>
public sealed class ToolRenderTests
{
	[Theory]
	[InlineData(HostKind.Desktop)]
	[InlineData(HostKind.Web)]
	public async Task Tool_ShowsTheThreeModesAndThePasswordOptions(HostKind host)
	{
		string html = await StaticRender.RenderAsync<PasswordGeneratorTool>([], host);

		foreach (string tab in new[] { "Password", "Passphrase", "PIN" })
		{
			Assert.Contains(tab, html, StringComparison.Ordinal);
		}

		foreach (string label in new[] { "Length", "Characters", "Rules", "How many", "Regenerate" })
		{
			Assert.Contains(label, html, StringComparison.Ordinal);
		}
	}

	[Fact]
	public async Task Tool_OpeningOnPassphrases_ShowsThePassphraseOptions()
	{
		TestSettings settings = new();
		settings.Set(new PasswordSettings { Mode = GenerationMode.Passphrase, ShortWords = true });

		string html = await StaticRender.RenderAsync<PasswordGeneratorTool>(
			services => services.AddSingleton<ISettingsService>(settings),
			[]);

		foreach (string label in new[] { "Words", "Shorter words", "Separator", "Extras" })
		{
			Assert.Contains(label, html, StringComparison.Ordinal);
		}

		Assert.Contains("1296", html, StringComparison.Ordinal);
		Assert.Contains(WordList.Small.Credit, html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Tool_OpeningOnPins_ShowsThePinOptions()
	{
		TestSettings settings = new();
		settings.Set(new PasswordSettings { Mode = GenerationMode.Pin });

		string html = await StaticRender.RenderAsync<PasswordGeneratorTool>(
			services => services.AddSingleton<ISettingsService>(settings),
			[]);

		Assert.Contains("Digits", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Length", html, StringComparison.Ordinal);
	}
}
