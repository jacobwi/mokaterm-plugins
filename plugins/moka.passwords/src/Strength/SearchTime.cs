using System.Globalization;

namespace Moka.Passwords.Strength;

/// <summary>How long trying every possibility takes, in words a person reads at a glance.</summary>
public static class SearchTime
{
	private const double SecondsPerYear = 365.25 * 24 * 3600;

	private const string Superscripts = "⁰¹²³⁴⁵⁶⁷⁸⁹";

	/// <summary>
	/// Trying all 2^<paramref name="bits"/> values at <paramref name="guessesPerSecond"/>: "under a second", "about 3 days",
	/// "about 12 million years", "about 10²⁴ years". Worked in logarithms, since a 128-character password has over 800 bits.
	/// </summary>
	public static string ToTryAll(double bits, double guessesPerSecond = StrengthScale.GuessesPerSecond)
	{
		if (!double.IsFinite(bits) || bits < 0)
		{
			return "under a second";
		}

		double log10Seconds = (bits * Math.Log10(2)) - Math.Log10(guessesPerSecond);
		if (log10Seconds < 0)
		{
			return "under a second";
		}

		// Each unit ends where its rounded count would reach the next one, so nothing reads "about 60 minutes".
		double seconds = Math.Pow(10, log10Seconds);
		if (seconds < 59.5)
		{
			return About(seconds, "second");
		}

		if (seconds < 59.5 * 60)
		{
			return About(seconds / 60, "minute");
		}

		if (seconds < 23.5 * 3600)
		{
			return About(seconds / 3600, "hour");
		}

		if (seconds < 364.5 * 86_400)
		{
			return About(seconds / 86_400, "day");
		}

		double log10Years = log10Seconds - Math.Log10(SecondsPerYear);
		double years = Math.Pow(10, log10Years);
		return years switch
		{
			< 999.5 => About(years, "year"),
			< 999.5e3 => About(years / 1e3, "thousand years", plural: false),
			< 999.5e6 => About(years / 1e6, "million years", plural: false),
			< 999.5e9 => About(years / 1e9, "billion years", plural: false),
			_ => $"about 10{Superscript((int)Math.Round(log10Years))} years",
		};
	}

	private static string About(double value, string unit, bool plural = true)
	{
		long rounded = (long)Math.Round(value, MidpointRounding.AwayFromZero);
		string name = plural && rounded != 1 ? unit + "s" : unit;
		return string.Create(CultureInfo.CurrentCulture, $"about {rounded} {name}");
	}

	private static string Superscript(int value) =>
		string.Concat(value.ToString(CultureInfo.InvariantCulture).Select(digit => Superscripts[digit - '0']));
}
