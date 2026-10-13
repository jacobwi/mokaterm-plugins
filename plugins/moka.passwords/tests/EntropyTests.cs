using System.Globalization;
using System.Numerics;
using Moka.Passwords.Generation;
using Moka.Passwords.Strength;

namespace Moka.Passwords.Tests;

public sealed class EntropyTests
{
	private const double Precision = 1e-9;

	[Fact]
	public void Password_WithoutTheRule_IsLengthTimesLog2OfTheAlphabet()
	{
		PasswordOptions options = new() { RequireEachKind = false };

		Assert.Equal(20 * Math.Log2(90), Entropy.OfPassword(options), Precision);
		Assert.Equal(16 * Math.Log2(26), Entropy.OfPassword(options with { Length = 16, Uppercase = false, Digits = false, Symbols = false }), Precision);
	}

	[Theory]
	[InlineData(new[] { 2, 3 }, 4)]
	[InlineData(new[] { 1, 2, 3 }, 5)]
	[InlineData(new[] { 1, 1, 1, 1 }, 4)]
	[InlineData(new[] { 3, 1, 2, 2 }, 6)]
	[InlineData(new[] { 4 }, 3)]
	public void Password_WithTheRule_CountsWhatCountingByHandFinds(int[] classSizes, int length)
	{
		// Character c belongs to the class whose range of numbers holds it.
		int[] classOf = [.. classSizes.SelectMany((size, index) => Enumerable.Repeat(index, size))];
		int total = classOf.Length;
		long holdingEveryClass = 0;
		for (long value = 0; value < (long)Math.Pow(total, length); value++)
		{
			HashSet<int> seen = [];
			long rest = value;
			for (int position = 0; position < length; position++)
			{
				seen.Add(classOf[rest % total]);
				rest /= total;
			}

			if (seen.Count == classSizes.Length)
			{
				holdingEveryClass++;
			}
		}

		Assert.Equal(new BigInteger(holdingEveryClass), Entropy.CountPasswords(classSizes, length, requireEach: true));
	}

	[Fact]
	public void Password_WithTheRule_LosesALittleOfTheWholeAlphabet()
	{
		PasswordOptions options = new();

		// log2 of 90^20 minus the strings missing a kind, worked out once by hand: 129.68905...
		Assert.Equal(129.68905033853216, Entropy.OfPassword(options), 1e-6);
		Assert.True(Entropy.OfPassword(options) < Entropy.OfPassword(options with { RequireEachKind = false }));
	}

	[Fact]
	public void Password_OfOneKind_IsTheSameWithOrWithoutTheRule()
	{
		PasswordOptions options = new() { Uppercase = false, Lowercase = false, Symbols = false, Length = 12 };

		Assert.Equal(Entropy.OfPassword(options with { RequireEachKind = false }), Entropy.OfPassword(options), Precision);
		Assert.Equal(12 * Math.Log2(10), Entropy.OfPassword(options), Precision);
	}

	[Fact]
	public void Password_OfTheLargestSettings_StaysFinite()
	{
		PasswordOptions options = new() { Length = PasswordOptions.MaxLength, SymbolSet = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~" };

		double bits = Entropy.OfPassword(options);

		Assert.True(double.IsFinite(bits));
		Assert.InRange(bits, 838, 128 * Math.Log2(94));
	}

	[Fact]
	public void Password_LeavingOutLookAlikes_CountsTheSmallerAlphabet()
	{
		PasswordOptions options = new() { AvoidLookAlikes = true, RequireEachKind = false };

		Assert.Equal(20 * Math.Log2(24 + 24 + 8 + 27), Entropy.OfPassword(options), Precision);
	}

	[Fact]
	public void Passphrase_IsWordsTimesLog2Of7776_PlusTheExtras()
	{
		PassphraseOptions options = new();

		Assert.Equal(12.92481250360578, Entropy.BitsPerWord, Precision);
		Assert.Equal(6 * Math.Log2(7776), Entropy.OfPassphrase(options), Precision);
		Assert.Equal(6 * Math.Log2(7776), Entropy.OfPassphrase(options with { Capitalize = true }), Precision);
		Assert.Equal((6 * Math.Log2(7776)) + Math.Log2(10 * 6), Entropy.OfPassphrase(options with { AddDigit = true }), Precision);
		Assert.Equal((6 * Math.Log2(7776)) + Math.Log2(2 * 6), Entropy.OfPassphrase(options with { AddSymbol = true, SymbolSet = "!#!" }), Precision);
		Assert.Equal(
			(3 * Math.Log2(7776)) + Math.Log2(10 * 3) + Math.Log2(28 * 3),
			Entropy.OfPassphrase(options with { Words = 3, AddDigit = true, AddSymbol = true }),
			Precision);
	}

	[Fact]
	public void Passphrase_WithBadOptions_IsRefused() =>
		Assert.Throws<ArgumentException>(() => Entropy.OfPassphrase(new PassphraseOptions { Words = 2 }));

	[Theory]
	[InlineData(4)]
	[InlineData(6)]
	[InlineData(12)]
	public void Pin_IsDigitsTimesLog2OfTen(int digits) => Assert.Equal(digits * Math.Log2(10), Entropy.OfPin(digits), Precision);

	[Theory]
	[InlineData(0, StrengthLevel.Weak)]
	[InlineData(49.99, StrengthLevel.Weak)]
	[InlineData(StrengthScale.FairFrom, StrengthLevel.Fair)]
	[InlineData(69.99, StrengthLevel.Fair)]
	[InlineData(StrengthScale.StrongFrom, StrengthLevel.Strong)]
	[InlineData(99.99, StrengthLevel.Strong)]
	[InlineData(StrengthScale.VeryStrongFrom, StrengthLevel.VeryStrong)]
	[InlineData(800, StrengthLevel.VeryStrong)]
	public void Levels_StartAtTheirThresholds(double bits, StrengthLevel level) => Assert.Equal(level, StrengthScale.LevelOf(bits));

	[Fact]
	public void Thresholds_AreTheOnesAskedFor()
	{
		Assert.Equal(50, StrengthScale.FairFrom);
		Assert.Equal(70, StrengthScale.StrongFrom);
		Assert.Equal(100, StrengthScale.VeryStrongFrom);
		Assert.Equal(1e11, StrengthScale.GuessesPerSecond);
	}

	[Fact]
	public void Report_RoundsTheBitsDown_SoTheyNeverPassTheirLabel()
	{
		StrengthReport report = StrengthReport.For(49.99);

		Assert.Equal(StrengthLevel.Weak, report.Level);
		Assert.Equal(49.9.ToString("0.0", CultureInfo.CurrentCulture), report.BitsText);
		Assert.Equal("Weak", report.Label);
	}

	[Theory]
	[InlineData(0, 0)]
	[InlineData(64, 50)]
	[InlineData(StrengthScale.FullBarBits, 100)]
	[InlineData(500, 100)]
	public void Bar_FillsUpTo128Bits(double bits, double percent) => Assert.Equal(percent, StrengthScale.BarPercent(bits), Precision);

	[Theory]
	[InlineData(0, "under a second")]
	[InlineData(36, "under a second")]
	[InlineData(41.4480996393695, "about 30 seconds")]
	[InlineData(50.67691832986539, "about 5 hours")]
	[InlineData(77.548875, "about 70 thousand years")]
	[InlineData(129.68905033853216, "about 10²¹ years")]
	[InlineData(200, "about 10⁴² years")]
	public void SearchTime_ReadsAtAGlance(double bits, string expected) => Assert.Equal(expected, SearchTime.ToTryAll(bits));

	[Fact]
	public void SearchTime_UsesTheUnitThatFits()
	{
		Assert.Equal("about 1 minute", SearchTime.ToTryAll(Math.Log2(60 * 1e11)));
		Assert.Equal("about 1 hour", SearchTime.ToTryAll(Math.Log2(3600 * 1e11)));
		Assert.Equal("about 3 days", SearchTime.ToTryAll(Math.Log2(3 * 86_400 * 1e11)));
		Assert.Equal("about 10 years", SearchTime.ToTryAll(Math.Log2(10 * 365.25 * 86_400 * 1e11)));
		Assert.Equal("about 5 million years", SearchTime.ToTryAll(Math.Log2(5e6 * 365.25 * 86_400 * 1e11)));
		Assert.Equal("about 7 billion years", SearchTime.ToTryAll(Math.Log2(7e9 * 365.25 * 86_400 * 1e11)));

		// Just under a minute rounds up to the next unit rather than reading "about 60 seconds".
		Assert.Equal("about 1 minute", SearchTime.ToTryAll(Math.Log2(59.7 * 1e11)));
	}
}
