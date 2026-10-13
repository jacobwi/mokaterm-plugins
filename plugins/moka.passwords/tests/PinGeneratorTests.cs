using Moka.Passwords.Generation;

namespace Moka.Passwords.Tests;

public sealed class PinGeneratorTests
{
	[Theory]
	[InlineData(PinGenerator.MinDigits)]
	[InlineData(PinGenerator.DefaultDigits)]
	[InlineData(PinGenerator.MaxDigits)]
	public void Pins_HaveTheLength_AndOnlyDigits(int digits)
	{
		Assert.Null(PinGenerator.ProblemWith(digits));
		for (int draw = 0; draw < 200; draw++)
		{
			string pin = PinGenerator.Generate(digits);

			Assert.Equal(digits, pin.Length);
			Assert.All(pin, c => Assert.True(char.IsAsciiDigit(c)));
		}
	}

	[Theory]
	[InlineData(PinGenerator.MinDigits - 1)]
	[InlineData(PinGenerator.MaxDigits + 1)]
	public void Lengths_OutsideTheRange_AreRefused(int digits)
	{
		Assert.NotNull(PinGenerator.ProblemWith(digits));
		Assert.Throws<ArgumentOutOfRangeException>(() => PinGenerator.Generate(digits));
	}

	[Fact]
	public void Digits_ComeFromThePicks()
	{
		ScriptedPicks picks = new(0, 9, 4, 2);

		Assert.Equal("0942", PinGenerator.Generate(4, picks.Pick));
	}

	// 120,000 digits is 12,000 a digit with a standard deviation near 104; 10% either way is over 11 deviations.
	[Fact]
	public void EveryDigit_StaysNearEven()
	{
		int[] counts = new int[10];
		for (int draw = 0; draw < 10_000; draw++)
		{
			foreach (char c in PinGenerator.Generate(PinGenerator.MaxDigits))
			{
				counts[c - '0']++;
			}
		}

		Assert.All(counts, count => Assert.InRange(count, 10_800, 13_200));
	}
}
