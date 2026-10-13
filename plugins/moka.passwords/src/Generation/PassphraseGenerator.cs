using System.Security.Cryptography;

namespace Moka.Passwords.Generation;

/// <summary>Draws passphrases: words from <see cref="WordList"/>, each as likely as any other, with the extras the options ask for.</summary>
public static class PassphraseGenerator
{
	public static string Generate(PassphraseOptions options) => Generate(options, RandomNumberGenerator.GetInt32);

	/// <summary>Draws with <paramref name="pick"/>, which returns a whole number from 0 up to, not including, its argument.</summary>
	/// <exception cref="ArgumentException">The options cannot make a passphrase.</exception>
	internal static string Generate(PassphraseOptions options, Func<int, int> pick)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(pick);
		if (options.Problem() is { } problem)
		{
			throw new ArgumentException(problem, nameof(options));
		}

		IReadOnlyList<string> list = WordList.Words;
		string[] words = new string[options.Words];
		for (int index = 0; index < words.Length; index++)
		{
			string word = list[pick(list.Count)];
			words[index] = options.Capitalize ? string.Concat(char.ToUpperInvariant(word[0]).ToString(), word.AsSpan(1)) : word;
		}

		if (options.AddDigit)
		{
			int at = pick(words.Length);
			words[at] += CharacterSets.Digits[pick(CharacterSets.Digits.Length)];
		}

		if (options.AddSymbol)
		{
			string symbols = CharacterSets.Distinct(options.SymbolSet);
			int at = pick(words.Length);
			words[at] += symbols[pick(symbols.Length)];
		}

		return string.Join(options.SeparatorText, words);
	}
}
