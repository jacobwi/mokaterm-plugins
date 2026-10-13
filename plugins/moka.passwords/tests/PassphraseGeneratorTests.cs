using Moka.Passwords.Generation;

namespace Moka.Passwords.Tests;

public sealed class PassphraseGeneratorTests
{
	private static readonly HashSet<string> Words = new(WordList.Words, StringComparer.Ordinal);

	[Theory]
	[InlineData(PassphraseSeparator.Space, " ")]
	[InlineData(PassphraseSeparator.Dot, ".")]
	[InlineData(PassphraseSeparator.Underscore, "_")]
	[InlineData(PassphraseSeparator.Custom, "+")]
	public void Words_ComeFromTheList_BetweenTheSeparator(PassphraseSeparator separator, string text)
	{
		PassphraseOptions options = new() { Words = 7, Separator = separator, CustomSeparator = "+" };

		Assert.Equal(text, options.SeparatorText);
		for (int draw = 0; draw < 300; draw++)
		{
			string[] words = PassphraseGenerator.Generate(options).Split(text);

			Assert.Equal(7, words.Length);
			Assert.All(words, word => Assert.Contains(word, Words));
		}
	}

	[Fact]
	public void Dash_JoinsTheWordsPicked()
	{
		PassphraseOptions options = new() { Words = 3, Separator = PassphraseSeparator.Dash };
		ScriptedPicks picks = new(0, 1, WordList.Count - 1);

		Assert.Equal("abacus-abdomen-zoom", PassphraseGenerator.Generate(options, picks.Pick));
	}

	[Fact]
	public void None_RunsTheWordsTogether()
	{
		PassphraseOptions options = new() { Words = 3, Separator = PassphraseSeparator.None };
		ScriptedPicks picks = new(2, 0, 1);

		Assert.Equal("abdominalabacusabdomen", PassphraseGenerator.Generate(options, picks.Pick));
		Assert.True(options.BoundariesAmbiguous);
		Assert.False((options with { Capitalize = true }).BoundariesAmbiguous);
		Assert.False((options with { Separator = PassphraseSeparator.Space }).BoundariesAmbiguous);
	}

	[Fact]
	public void Extras_GoAfterTheWordsPicked_DigitBeforeSymbol()
	{
		PassphraseOptions options = new() { Words = 3, Capitalize = true, AddDigit = true, AddSymbol = true };

		// Three words, then the digit's word and value, then the symbol's word and its place in the default set ("!").
		ScriptedPicks picks = new(0, 1, 2, 1, 7, 1, 0);

		Assert.Equal("Abacus-Abdomen7!-Abdominal", PassphraseGenerator.Generate(options, picks.Pick));
		Assert.Equal(7, picks.Used);
	}

	[Fact]
	public void Extras_CanLandOnDifferentWords()
	{
		PassphraseOptions options = new() { Words = 3, AddDigit = true, AddSymbol = true, SymbolSet = "#@", Separator = PassphraseSeparator.Space };
		ScriptedPicks picks = new(0, 1, 2, 2, 3, 0, 1);

		Assert.Equal("abacus@ abdomen abdominal3", PassphraseGenerator.Generate(options, picks.Pick));
	}

	[Fact]
	public void Extras_AppearExactlyOnce()
	{
		PassphraseOptions options = new() { Words = 5, AddDigit = true, AddSymbol = true, SymbolSet = "!", Separator = PassphraseSeparator.Space };

		for (int draw = 0; draw < 500; draw++)
		{
			string passphrase = PassphraseGenerator.Generate(options);

			Assert.Equal(1, passphrase.Count(char.IsAsciiDigit));
			Assert.Equal(1, passphrase.Count(c => c == '!'));
			Assert.All(passphrase.Split(' '), word => Assert.Contains(string.Concat(word.Where(c => char.IsAsciiLetter(c) || c == '-')), Words));
		}
	}

	[Fact]
	public void WithoutExtras_ThereAreNoDigitsOrSymbols()
	{
		PassphraseOptions options = new() { Separator = PassphraseSeparator.Space };

		for (int draw = 0; draw < 300; draw++)
		{
			Assert.All(PassphraseGenerator.Generate(options).Split(' '), word => Assert.Matches("^[a-z]+(-[a-z]+)*$", word));
		}
	}

	[Fact]
	public void Capitalize_StartsEveryWordWithACapital()
	{
		PassphraseOptions options = new() { Words = 8, Capitalize = true, Separator = PassphraseSeparator.Space };

		for (int draw = 0; draw < 300; draw++)
		{
			Assert.All(PassphraseGenerator.Generate(options).Split(' '), word =>
			{
				Assert.True(char.IsAsciiLetterUpper(word[0]));
				Assert.Contains(string.Concat(char.ToLowerInvariant(word[0]).ToString(), word.AsSpan(1)), Words);
			});
		}
	}

	[Theory]
	[InlineData(PassphraseOptions.MinWords)]
	[InlineData(PassphraseOptions.MaxWords)]
	public void WordCounts_InsideTheRange_AreMade(int count)
	{
		PassphraseOptions options = new() { Words = count, Separator = PassphraseSeparator.Space };

		Assert.Null(options.Problem());
		Assert.Equal(count, PassphraseGenerator.Generate(options).Split(' ').Length);
	}

	[Theory]
	[InlineData(PassphraseOptions.MinWords - 1)]
	[InlineData(PassphraseOptions.MaxWords + 1)]
	public void WordCounts_OutsideTheRange_AreRefused(int count)
	{
		PassphraseOptions options = new() { Words = count };

		Assert.NotNull(options.Problem());
		Assert.Throws<ArgumentException>(() => PassphraseGenerator.Generate(options));
	}

	[Theory]
	[InlineData("")]
	[InlineData("x")]
	[InlineData("+a")]
	[InlineData(" ")]
	[InlineData("+++++")]
	[InlineData("é")]
	public void CustomSeparators_ThatBlurTheWords_AreRefused(string separator) =>
		Assert.NotNull(PassphraseOptions.CustomSeparatorProblem(separator));

	[Theory]
	[InlineData("+")]
	[InlineData("--")]
	[InlineData("7")]
	[InlineData("#1#")]
	[InlineData("::::")]
	public void CustomSeparators_OfSymbolsAndDigits_AreTaken(string separator) =>
		Assert.Null(PassphraseOptions.CustomSeparatorProblem(separator));

	[Fact]
	public void CustomSeparator_IsOnlyCheckedWhenPicked()
	{
		PassphraseOptions options = new() { Separator = PassphraseSeparator.Custom };

		Assert.Equal("Type a separator.", options.Problem());
		Assert.Null((options with { Separator = PassphraseSeparator.Dot }).Problem());
	}

	[Fact]
	public void SymbolSet_IsOnlyCheckedWhenASymbolIsAdded()
	{
		PassphraseOptions options = new() { SymbolSet = "abc" };

		Assert.Null(options.Problem());
		Assert.NotNull((options with { AddSymbol = true }).Problem());
	}
}
