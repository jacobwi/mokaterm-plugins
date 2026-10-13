using Moka.Passwords.Generation;
using Mokaterm.Abstractions.Settings;

namespace Moka.Passwords.Settings;

/// <summary>
/// What the tool starts with, under <c>plugin.moka.passwords</c> in settings.json. Only options are kept here, never a
/// generated value. Every range is a Min and Max pair here, the generators' own, read by <see cref="Clamped"/>, by the
/// settings page and by the tool.
/// </summary>
public sealed record PasswordSettings : ISettingsSection
{
	public const int MinLength = PasswordOptions.MinLength;

	public const int MaxLength = PasswordOptions.MaxLength;

	public const int MinWords = PassphraseOptions.MinWords;

	public const int MaxWords = PassphraseOptions.MaxWords;

	public const int MinPinDigits = PinGenerator.MinDigits;

	public const int MaxPinDigits = PinGenerator.MaxDigits;

	/// <summary>How many values the tool makes at once.</summary>
	public const int MinCount = 1;

	public const int MaxCount = 20;

	public static string SectionKey => "plugin.moka.passwords";

	/// <summary>Which kind the tool opens on.</summary>
	public GenerationMode Mode { get; init; } = GenerationMode.Password;

	public int Length { get; init; } = PasswordOptions.DefaultLength;

	public bool Lowercase { get; init; } = true;

	public bool Uppercase { get; init; } = true;

	public bool Digits { get; init; } = true;

	public bool Symbols { get; init; } = true;

	/// <summary>The symbols passwords draw from, and passphrases when they add one.</summary>
	public string SymbolSet { get; init; } = CharacterSets.DefaultSymbols;

	public bool AvoidLookAlikes { get; init; }

	public bool RequireEachKind { get; init; } = true;

	public int Words { get; init; } = PassphraseOptions.DefaultWords;

	public PassphraseSeparator Separator { get; init; } = PassphraseSeparator.Dash;

	public string CustomSeparator { get; init; } = "";

	public bool Capitalize { get; init; }

	public bool AddDigit { get; init; }

	public bool AddSymbol { get; init; }

	public int PinDigits { get; init; } = PinGenerator.DefaultDigits;

	public int Count { get; init; } = 5;

	/// <summary>The values start hidden, for a screen someone else can see.</summary>
	public bool StartHidden { get; init; }

	/// <summary>A copy with every value inside what the page allows, since settings.json can be edited by hand.</summary>
	public PasswordSettings Clamped()
	{
		// Custom stays picked without a separator of its own: the page offers the field only once it is picked, and the
		// tool asks for one.
		bool anyKind = Lowercase || Uppercase || Digits || Symbols;
		return this with
		{
			Mode = Enum.IsDefined(Mode) ? Mode : GenerationMode.Password,
			Length = Math.Clamp(Length, MinLength, MaxLength),
			Lowercase = Lowercase || !anyKind,
			Uppercase = Uppercase || !anyKind,
			Digits = Digits || !anyKind,
			Symbols = Symbols || !anyKind,
			SymbolSet = CharacterSets.SymbolSetProblem(SymbolSet) is null ? SymbolSet : CharacterSets.DefaultSymbols,
			Words = Math.Clamp(Words, MinWords, MaxWords),
			Separator = Enum.IsDefined(Separator) ? Separator : PassphraseSeparator.Dash,
			CustomSeparator = PassphraseOptions.CustomSeparatorProblem(CustomSeparator) is null ? CustomSeparator : "",
			PinDigits = Math.Clamp(PinDigits, MinPinDigits, MaxPinDigits),
			Count = Math.Clamp(Count, MinCount, MaxCount),
		};
	}

	public PasswordOptions ToPasswordOptions() => new()
	{
		Length = Length,
		Lowercase = Lowercase,
		Uppercase = Uppercase,
		Digits = Digits,
		Symbols = Symbols,
		SymbolSet = SymbolSet,
		AvoidLookAlikes = AvoidLookAlikes,
		RequireEachKind = RequireEachKind,
	};

	public PassphraseOptions ToPassphraseOptions() => new()
	{
		Words = Words,
		Separator = Separator,
		CustomSeparator = CustomSeparator,
		Capitalize = Capitalize,
		AddDigit = AddDigit,
		AddSymbol = AddSymbol,
		SymbolSet = SymbolSet,
	};
}
