namespace Moka.Passwords.Generation;

/// <summary>What a passphrase is made of. <see cref="Problem"/> says whether the options can be used.</summary>
public sealed record PassphraseOptions
{
	public const int MinWords = 3;

	public const int MaxWords = 12;

	public const int DefaultWords = 6;

	public const int MaxCustomSeparatorLength = 4;

	public int Words { get; init; } = DefaultWords;

	public PassphraseSeparator Separator { get; init; } = PassphraseSeparator.Dash;

	/// <summary>The separator while <see cref="Separator"/> is <see cref="PassphraseSeparator.Custom"/>.</summary>
	public string CustomSeparator { get; init; } = "";

	/// <summary>Each word starts with a capital. It changes nothing about the strength, since every word gets one.</summary>
	public bool Capitalize { get; init; }

	/// <summary>A random digit goes after a random word.</summary>
	public bool AddDigit { get; init; }

	/// <summary>A random symbol from <see cref="SymbolSet"/> goes after a random word, after the digit if both land on one.</summary>
	public bool AddSymbol { get; init; }

	/// <summary>The symbols to draw from while <see cref="AddSymbol"/> is on; repeats count once.</summary>
	public string SymbolSet { get; init; } = CharacterSets.DefaultSymbols;

	/// <summary>The text between two words.</summary>
	public string SeparatorText => Separator switch
	{
		PassphraseSeparator.Space => " ",
		PassphraseSeparator.Dash => "-",
		PassphraseSeparator.Dot => ".",
		PassphraseSeparator.Underscore => "_",
		PassphraseSeparator.None => "",
		_ => CustomSeparator,
	};

	/// <summary>
	/// Whether different words can spell the same passphrase. Without a separator or capitals the word boundaries are
	/// lost, so a few passphrases can be made two ways and the strength is an upper bound. The four words with a dash in
	/// the list do not collide behind a dash separator, because none of them splits into two words of the list.
	/// </summary>
	public bool BoundariesAmbiguous => SeparatorText.Length == 0 && !Capitalize;

	/// <summary>Why the text cannot separate words, or null when it can.</summary>
	public static string? CustomSeparatorProblem(string? text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "Type a separator.";
		}

		if (text.Length > MaxCustomSeparatorLength)
		{
			return $"At most {MaxCustomSeparatorLength} characters.";
		}

		// Letters would run into the words and make two different phrases read the same.
		return text.All(c => CharacterSets.IsSymbol(c) || char.IsAsciiDigit(c)) ? null : "Symbols and digits only: no letters or spaces.";
	}

	/// <summary>Why the options cannot make a passphrase, or null when they can.</summary>
	public string? Problem()
	{
		if (Words is < MinWords or > MaxWords)
		{
			return $"A passphrase has {MinWords} to {MaxWords} words.";
		}

		if (Separator == PassphraseSeparator.Custom && CustomSeparatorProblem(CustomSeparator) is { } separatorProblem)
		{
			return separatorProblem;
		}

		return AddSymbol ? CharacterSets.SymbolSetProblem(SymbolSet) : null;
	}
}
