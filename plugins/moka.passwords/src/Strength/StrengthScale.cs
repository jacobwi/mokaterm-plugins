namespace Moka.Passwords.Strength;

/// <summary>The one place the labels' thresholds, the bar's scale and the guessing rate are set.</summary>
public static class StrengthScale
{
	public const double FairFrom = 50;

	public const double StrongFrom = 70;

	public const double VeryStrongFrom = 100;

	/// <summary>The bits at which the bar is full: a 128-bit key, past which more bits change nothing in practice.</summary>
	public const double FullBarBits = 128;

	/// <summary>An offline attack on a fast hash with a rack of GPUs: 10^11 guesses a second.</summary>
	public const double GuessesPerSecond = 1e11;

	/// <summary>How <see cref="GuessesPerSecond"/> is written for people.</summary>
	public const string RateText = "100 billion guesses a second";

	public static StrengthLevel LevelOf(double bits) => bits switch
	{
		>= VeryStrongFrom => StrengthLevel.VeryStrong,
		>= StrongFrom => StrengthLevel.Strong,
		>= FairFrom => StrengthLevel.Fair,
		_ => StrengthLevel.Weak,
	};

	public static string Label(StrengthLevel level) => level switch
	{
		StrengthLevel.VeryStrong => "Very strong",
		StrengthLevel.Strong => "Strong",
		StrengthLevel.Fair => "Fair",
		_ => "Weak",
	};

	/// <summary>How full the bar is, from 0 to 100.</summary>
	public static double BarPercent(double bits) => double.IsFinite(bits) ? Math.Clamp(bits / FullBarBits * 100, 0, 100) : 0;
}
