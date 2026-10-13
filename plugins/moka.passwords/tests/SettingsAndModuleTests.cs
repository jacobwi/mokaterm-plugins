using Microsoft.Extensions.DependencyInjection;
using Moka.Passwords.Generation;
using Moka.Passwords.Settings;
using Moka.Passwords.Tool;
using Mokaterm.Abstractions.Settings;
using Mokaterm.UI.Common.Contributions;

namespace Moka.Passwords.Tests;

public sealed class SettingsAndModuleTests
{
	[Fact]
	public void Clamped_HoldsEveryValueToItsRange()
	{
		PasswordSettings wild = new()
		{
			Mode = (GenerationMode)9,
			Length = 3,
			Words = 99,
			PinDigits = 1,
			Count = 0,
			Separator = (PassphraseSeparator)42,
			CustomSeparator = "abc",
			SymbolSet = "letters",
		};

		PasswordSettings clamped = wild.Clamped();

		Assert.Equal(GenerationMode.Password, clamped.Mode);
		Assert.Equal(PasswordSettings.MinLength, clamped.Length);
		Assert.Equal(PasswordSettings.MaxWords, clamped.Words);
		Assert.Equal(PasswordSettings.MinPinDigits, clamped.PinDigits);
		Assert.Equal(PasswordSettings.MinCount, clamped.Count);
		Assert.Equal(PassphraseSeparator.Dash, clamped.Separator);
		Assert.Equal("", clamped.CustomSeparator);
		Assert.Equal(CharacterSets.DefaultSymbols, clamped.SymbolSet);
		Assert.Equal(PasswordSettings.MaxCount, (new PasswordSettings { Count = int.MaxValue }).Clamped().Count);
		Assert.Equal(new PasswordSettings(), new PasswordSettings().Clamped());
	}

	[Fact]
	public void Clamped_TurnsEveryKindBackOn_WhenNoneIsLeft()
	{
		PasswordSettings clamped = new PasswordSettings { Lowercase = false, Uppercase = false, Digits = false, Symbols = false }.Clamped();

		Assert.Equal(4, clamped.ToPasswordOptions().KindCount);
		Assert.False(new PasswordSettings { Lowercase = false, Uppercase = false, Digits = false }.Clamped().Lowercase);
	}

	[Fact]
	public void Clamped_KeepsCustomPicked_WhileItsSeparatorIsStillToBeTyped()
	{
		PasswordSettings clamped = new PasswordSettings { Separator = PassphraseSeparator.Custom, CustomSeparator = "" }.Clamped();

		Assert.Equal(PassphraseSeparator.Custom, clamped.Separator);
		Assert.Equal("Type a separator.", clamped.ToPassphraseOptions().Problem());
	}

	[Fact]
	public void Settings_BecomeTheGeneratorsOptions()
	{
		PasswordSettings settings = new()
		{
			Length = 32,
			Symbols = false,
			SymbolSet = "!@",
			AvoidLookAlikes = true,
			RequireEachKind = false,
			Words = 4,
			Separator = PassphraseSeparator.Custom,
			CustomSeparator = "+",
			Capitalize = true,
			AddDigit = true,
			AddSymbol = true,
		};

		Assert.Equal(
			new PasswordOptions { Length = 32, Symbols = false, SymbolSet = "!@", AvoidLookAlikes = true, RequireEachKind = false },
			settings.ToPasswordOptions());
		Assert.Equal(
			new PassphraseOptions { Words = 4, Separator = PassphraseSeparator.Custom, CustomSeparator = "+", Capitalize = true, AddDigit = true, AddSymbol = true, SymbolSet = "!@" },
			settings.ToPassphraseOptions());
	}

	[Fact]
	public void Defaults_AreTheOnesAskedFor()
	{
		PasswordSettings settings = new();

		Assert.Equal(20, settings.Length);
		Assert.Equal(6, settings.Words);
		Assert.Equal((8, 128), (PasswordSettings.MinLength, PasswordSettings.MaxLength));
		Assert.Equal((3, 12), (PasswordSettings.MinWords, PasswordSettings.MaxWords));
		Assert.Equal((4, 12), (PasswordSettings.MinPinDigits, PasswordSettings.MaxPinDigits));
		Assert.Equal((1, 20), (PasswordSettings.MinCount, PasswordSettings.MaxCount));
		Assert.Null(PasswordAlphabet.ProblemWith(settings.ToPasswordOptions()));
		Assert.Null(settings.ToPassphraseOptions().Problem());
	}

	[Fact]
	public void SectionKey_IsUnderThePluginsOwnName() => Assert.Equal("plugin.moka.passwords", PasswordSettings.SectionKey);

	[Fact]
	public void Module_RegistersTheToolAndTheSettings()
	{
		ServiceCollection services = new();

		new PasswordsModule().ConfigureServices(services);
		using ServiceProvider provider = services.BuildServiceProvider();

		AppToolDescriptor tool = Assert.Single(provider.GetServices<AppToolDescriptor>());
		Assert.Equal("password-generator", tool.Id);
		Assert.Equal(typeof(PasswordGeneratorTool), tool.Component);
		Assert.Equal(SessionToolSize.Standard, tool.Size);

		SettingsPageDescriptor settings = Assert.Single(provider.GetServices<SettingsPageDescriptor>());
		Assert.Equal(PasswordSettings.SectionKey, settings.Id);
		Assert.Equal(SettingsPageGroup.Plugins, settings.Group);
		Assert.Equal(typeof(PasswordSettingsPage), settings.Component);
		Assert.Equal(PasswordSettings.SectionKey, Assert.Single(provider.GetServices<SettingsSectionType>()).Key);
		Assert.Equal("moka.passwords", new PasswordsModule().Info.Id);
	}
}
