using Moka.Passwords.Generation;

namespace Moka.Passwords.Tests;

public sealed class PasswordGeneratorTests
{
	public static TheoryData<bool, bool, bool, bool> KindSets()
	{
		TheoryData<bool, bool, bool, bool> sets = [];
		for (int mask = 1; mask < 16; mask++)
		{
			sets.Add((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0);
		}

		return sets;
	}

	[Theory]
	[MemberData(nameof(KindSets))]
	public void EveryDraw_HoldsEveryChosenKind_AndNothingElse(bool lower, bool upper, bool digits, bool symbols)
	{
		PasswordOptions options = new()
		{
			Length = PasswordOptions.MinLength,
			Lowercase = lower,
			Uppercase = upper,
			Digits = digits,
			Symbols = symbols,
			SymbolSet = "!",
		};
		PasswordAlphabet alphabet = PasswordAlphabet.For(options);

		for (int draw = 0; draw < 300; draw++)
		{
			string password = PasswordGenerator.Generate(options);

			Assert.Equal(options.Length, password.Length);
			Assert.All(password, c => Assert.Contains(c, alphabet.Characters));
			Assert.Equal(lower, password.Any(char.IsAsciiLetterLower));
			Assert.Equal(upper, password.Any(char.IsAsciiLetterUpper));
			Assert.Equal(digits, password.Any(char.IsAsciiDigit));
			Assert.Equal(symbols, password.Contains('!', StringComparison.Ordinal));
		}
	}

	[Fact]
	public void LookAlikes_NeverAppear()
	{
		PasswordOptions options = new() { Length = 64, AvoidLookAlikes = true };
		PasswordAlphabet alphabet = PasswordAlphabet.For(options);

		Assert.DoesNotContain(alphabet.Characters, c => CharacterSets.LookAlikes.Contains(c, StringComparison.Ordinal));
		for (int draw = 0; draw < 2000; draw++)
		{
			string password = PasswordGenerator.Generate(options);
			Assert.DoesNotContain(password, c => CharacterSets.LookAlikes.Contains(c, StringComparison.Ordinal));
		}
	}

	// 256,000 characters over 90 is about 2,844 a character with a standard deviation near 53. The bounds are 25% either
	// way, over 13 deviations, which no fair draw reaches; a biased pick (modulo, a forced character) would.
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void EveryCharacter_Appears_AndCountsStayNearEven(bool avoidLookAlikes)
	{
		PasswordOptions options = new() { Length = PasswordOptions.MaxLength, RequireEachKind = false, AvoidLookAlikes = avoidLookAlikes };
		PasswordAlphabet alphabet = PasswordAlphabet.For(options);
		Dictionary<char, int> counts = alphabet.Characters.ToDictionary(c => c, _ => 0);
		const int Draws = 2000;

		for (int draw = 0; draw < Draws; draw++)
		{
			foreach (char c in PasswordGenerator.Generate(options))
			{
				counts[c]++;
			}
		}

		double expected = Draws * (double)options.Length / alphabet.Size;
		Assert.All(counts, pair => Assert.InRange(pair.Value, expected * 0.75, expected * 1.25));
	}

	// With the rule, each of 90 characters is expected about 440 times in 40,000; missing one entirely is out of reach.
	[Fact]
	public void WithTheRule_EveryCharacterStillAppears()
	{
		PasswordOptions options = new() { Length = PasswordOptions.MinLength };
		PasswordAlphabet alphabet = PasswordAlphabet.For(options);
		HashSet<char> seen = [];

		for (int draw = 0; draw < 5000; draw++)
		{
			seen.UnionWith(PasswordGenerator.Generate(options));
		}

		Assert.Equal(alphabet.Characters.Order(), seen.Order());
	}

	[Fact]
	public void MissingKind_IsDrawnAgainWhole_NotPatched()
	{
		PasswordOptions options = new() { Length = 8, Uppercase = false, Symbols = false };
		ScriptedPicks picks = new(0, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 5, 6, 7, 26);

		string password = PasswordGenerator.Generate(options, picks.Pick);

		Assert.Equal("bcdefgh0", password);
		Assert.Equal(16, picks.Used);
	}

	[Fact]
	public void WithoutTheRule_TheFirstDrawIsKept()
	{
		PasswordOptions options = new() { Length = 8, RequireEachKind = false };
		ScriptedPicks picks = new(0, 0, 0, 0, 0, 0, 0, 0);

		Assert.Equal("aaaaaaaa", PasswordGenerator.Generate(options, picks.Pick));
	}

	[Theory]
	[InlineData(PasswordOptions.MinLength)]
	[InlineData(PasswordOptions.DefaultLength)]
	[InlineData(PasswordOptions.MaxLength)]
	public void Lengths_InsideTheRange_AreMade(int length)
	{
		PasswordOptions options = new() { Length = length };

		Assert.Null(PasswordAlphabet.ProblemWith(options));
		Assert.Equal(length, PasswordGenerator.Generate(options).Length);
	}

	[Theory]
	[InlineData(PasswordOptions.MinLength - 1)]
	[InlineData(PasswordOptions.MaxLength + 1)]
	[InlineData(0)]
	public void Lengths_OutsideTheRange_AreRefused(int length)
	{
		PasswordOptions options = new() { Length = length };

		Assert.NotNull(PasswordAlphabet.ProblemWith(options));
		Assert.Throws<ArgumentException>(() => PasswordGenerator.Generate(options));
	}

	[Fact]
	public void TheShortestLength_HoldsEveryKind() =>
		Assert.True(PasswordOptions.MinLength >= Enum.GetValues<CharacterKind>().Length);

	[Fact]
	public void NoKind_IsRefused()
	{
		PasswordOptions options = new() { Lowercase = false, Uppercase = false, Digits = false, Symbols = false };

		Assert.Equal(0, options.KindCount);
		Assert.Equal("Choose at least one kind of character.", PasswordAlphabet.ProblemWith(options));
	}

	[Theory]
	[InlineData("")]
	[InlineData("abc")]
	[InlineData("!1")]
	[InlineData("! ")]
	[InlineData("!é")]
	[InlineData("!\t")]
	public void SymbolSets_WithAnythingButAsciiSymbols_AreRefused(string set) => Assert.NotNull(CharacterSets.SymbolSetProblem(set));

	[Theory]
	[InlineData("!")]
	[InlineData(CharacterSets.DefaultSymbols)]
	[InlineData("!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~")]
	public void SymbolSets_OfAsciiSymbols_AreTaken(string set) => Assert.Null(CharacterSets.SymbolSetProblem(set));

	[Fact]
	public void SymbolSet_RepeatsCountOnce()
	{
		PasswordOptions options = new() { Lowercase = false, Uppercase = false, Digits = false, SymbolSet = "!!@!@" };

		Assert.Equal("!@", PasswordAlphabet.For(options).Characters);
	}

	[Fact]
	public void SymbolSet_OfLookAlikesOnly_LeavesNothingAndIsRefused()
	{
		PasswordOptions options = new() { SymbolSet = "|", AvoidLookAlikes = true };

		Assert.NotNull(PasswordAlphabet.ProblemWith(options));
		Assert.Null(PasswordAlphabet.ProblemWith(options with { AvoidLookAlikes = false }));
		Assert.Null(PasswordAlphabet.ProblemWith(options with { Symbols = false }));
	}

	[Fact]
	public void DefaultOptions_DrawFromNinetyCharacters()
	{
		Assert.Equal(26 + 26 + 10 + 28, PasswordAlphabet.For(new PasswordOptions()).Size);
		Assert.Equal(24 + 24 + 8 + 27, PasswordAlphabet.For(new PasswordOptions { AvoidLookAlikes = true }).Size);
	}

	[Fact]
	public void Kinds_NeverShareACharacter()
	{
		PasswordAlphabet alphabet = PasswordAlphabet.For(new PasswordOptions { SymbolSet = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~" });

		Assert.Equal(alphabet.Size, alphabet.Characters.Distinct().Count());
		Assert.Equal(94, alphabet.Size);
	}
}
