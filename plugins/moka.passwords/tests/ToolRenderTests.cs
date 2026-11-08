using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Moka.Passwords.Components;
using Moka.Passwords.Generation;
using Moka.Passwords.Settings;
using Moka.Passwords.Strength;
using Moka.Passwords.Tool;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Abstractions.Settings;
using Mokaterm.Plugin.TestHost;

namespace Moka.Passwords.Tests;

/// <summary>
/// The generator tool renders on both hosts: the three mode tabs, the options of the mode it opens on in their cards, the
/// strength as a ring with its word, and the values in a table with a secret copy per row.
/// </summary>
public sealed partial class ToolRenderTests
{
	[Theory]
	[InlineData(HostKind.Desktop)]
	[InlineData(HostKind.Web)]
	public async Task Tool_ShowsTheThreeModesAndThePasswordOptions(HostKind host)
	{
		string html = await StaticRender.RenderAsync<PasswordGeneratorTool>([], host, TestNavigation.Add);

		foreach (string tab in new[] { "Password", "Passphrase", "PIN" })
		{
			Assert.Contains(tab, html, StringComparison.Ordinal);
		}

		foreach (string label in new[] { "Length", "Characters", "Rules", "How many", "Regenerate" })
		{
			Assert.Contains(label, html, StringComparison.Ordinal);
		}

		Assert.Contains("moka-page-header", html, StringComparison.Ordinal);
		Assert.Contains("moka-card--inner", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Tool_ShowsTheStrengthAsARingWithItsWordAndTheFigures()
	{
		// Decoded, because the renderer writes the superscripts of a time such as "about 10²¹ years" as entities.
		string html = WebUtility.HtmlDecode(await RenderAsync(new PasswordSettings { Length = 20 }));
		StrengthReport report = StrengthReport.For(Entropy.OfPassword(new PasswordSettings { Length = 20 }.ToPasswordOptions()));

		Assert.Contains("moka-gauge--ring", html, StringComparison.Ordinal);
		Assert.Contains("moka-gauge--success", html, StringComparison.Ordinal);
		Assert.Contains(report.Label, html, StringComparison.Ordinal);
		Assert.Contains(report.BitsText, html, StringComparison.Ordinal);
		Assert.Contains("Entropy", html, StringComparison.Ordinal);
		Assert.Contains(report.TimeToTryAll, html, StringComparison.Ordinal);
		Assert.Contains("To try every one", html, StringComparison.Ordinal);
		Assert.Contains(StrengthScale.RateText, html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Tool_ListsTheValuesInATableWithASecretCopyPerRow()
	{
		string html = await RenderAsync(new PasswordSettings { Count = 3 });

		Assert.Contains("aria-label=\"Passwords\"", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Copy password 1\"", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Copy password 3\"", html, StringComparison.Ordinal);
		Assert.DoesNotContain("aria-label=\"Copy password 4\"", html, StringComparison.Ordinal);
		Assert.Equal(3, ValueSpans().Count(html));
	}

	[Fact]
	public async Task Tool_ShowsWhereThePasswordsDrawFrom()
	{
		string html = await RenderAsync(new PasswordSettings { AvoidLookAlikes = false });

		Assert.Contains("Drawn from", html, StringComparison.Ordinal);
		Assert.Contains("moka-donutchart", html, StringComparison.Ordinal);
		Assert.Contains("a-z 26", html, StringComparison.Ordinal);
		Assert.Contains("0-9 10", html, StringComparison.Ordinal);
		Assert.Contains($"Symbols {CharacterSets.Distinct(CharacterSets.DefaultSymbols).Length}", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Tool_StartingHidden_ShowsNoValue()
	{
		string html = await RenderAsync(new PasswordSettings { Count = 2, StartHidden = true });

		Assert.Equal(2, Regex.Count(html, "aria-label=\"Hidden\"", RegexOptions.None, TimeSpan.FromSeconds(1)));
		Assert.Equal(0, ValueSpans().Count(html));
		Assert.Contains(">Show", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Tool_WhenTheOptionsCannotMakeAValue_SaysWhyAndListsNothing()
	{
		string html = await RenderAsync(new PasswordSettings { Lowercase = false, Uppercase = false, Digits = false, Symbols = true, SymbolSet = "|", AvoidLookAlikes = true });

		Assert.Contains("Every symbol in the set is a look-alike", html, StringComparison.Ordinal);
		Assert.Contains("Nothing to make", html, StringComparison.Ordinal);
		Assert.DoesNotContain("moka-gauge", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Drawn from", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Tool_OpeningOnPassphrases_ShowsThePassphraseOptions()
	{
		string html = await RenderAsync(new PasswordSettings { Mode = GenerationMode.Passphrase, ShortWords = true });

		foreach (string label in new[] { "Words", "Shorter words", "Separator", "Extras", "Passphrases" })
		{
			Assert.Contains(label, html, StringComparison.Ordinal);
		}

		Assert.Contains("1296", html, StringComparison.Ordinal);
		Assert.Contains(WordList.Small.Credit, html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Copy passphrase 1\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Tool_OpeningOnPins_ShowsThePinOptionsAndTheLockOutNote()
	{
		string html = await RenderAsync(new PasswordSettings { Mode = GenerationMode.Pin });

		Assert.Contains("Digits", html, StringComparison.Ordinal);
		Assert.DoesNotContain("Length", html, StringComparison.Ordinal);
		Assert.Contains("moka-callout", html, StringComparison.Ordinal);
		Assert.Contains("A PIN only holds up where the device locks after a few wrong tries.", html, StringComparison.Ordinal);
		Assert.Contains("moka-gauge--error", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Copy PIN 1\"", html, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(40, "Weak", "error")]
	[InlineData(60, "Fair", "warning")]
	[InlineData(80, "Strong", "success")]
	[InlineData(300, "Very strong", "success")]
	public async Task StrengthMeter_ColoursTheRingByItsLabel(double bits, string label, string color)
	{
		string html = await StaticRender.RenderAsync<StrengthMeter>(new Dictionary<string, object?> { ["Report"] = StrengthReport.For(bits) });

		Assert.Contains($"moka-gauge--{color}", html, StringComparison.Ordinal);
		Assert.Contains($">{label}<", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Strength in bits\"", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync(PasswordSettings section)
	{
		TestSettings settings = new();
		settings.Set(section);
		return StaticRender.RenderAsync<PasswordGeneratorTool>(
			services =>
			{
				services.AddSingleton<ISettingsService>(settings);
				TestNavigation.Add(services);
			},
			[]);
	}

	[GeneratedRegex("class=\"mp-value\"", RegexOptions.None, 1000)]
	private static partial Regex ValueSpans();
}
