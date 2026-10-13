using System.Globalization;

namespace Moka.Passwords.Strength;

/// <summary>What the strength panel shows for one set of settings.</summary>
public sealed record StrengthReport(double Bits, StrengthLevel Level, string TimeToTryAll)
{
	public static StrengthReport For(double bits) => new(bits, StrengthScale.LevelOf(bits), SearchTime.ToTryAll(bits));

	public string Label => StrengthScale.Label(Level);

	public double BarPercent => StrengthScale.BarPercent(Bits);

	/// <summary>The bits to one decimal, rounded down so the number never reaches a threshold its label has not.</summary>
	public string BitsText => (Math.Floor(Bits * 10) / 10).ToString("0.0", CultureInfo.CurrentCulture);
}
