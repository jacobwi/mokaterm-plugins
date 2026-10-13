using System.Security.Cryptography;

namespace Moka.Passwords.Generation;

/// <summary>Draws PINs: every digit independent and each of the ten as likely as any other.</summary>
public static class PinGenerator
{
	public const int MinDigits = 4;

	public const int MaxDigits = 12;

	public const int DefaultDigits = 6;

	/// <summary>Why the length cannot be used, or null when it can.</summary>
	public static string? ProblemWith(int digits) =>
		digits is < MinDigits or > MaxDigits ? $"A PIN has {MinDigits} to {MaxDigits} digits." : null;

	public static string Generate(int digits) => Generate(digits, RandomNumberGenerator.GetInt32);

	/// <summary>Draws with <paramref name="pick"/>, which returns a whole number from 0 up to, not including, its argument.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The length is outside <see cref="MinDigits"/> to <see cref="MaxDigits"/>.</exception>
	internal static string Generate(int digits, Func<int, int> pick)
	{
		ArgumentNullException.ThrowIfNull(pick);
		ArgumentOutOfRangeException.ThrowIfLessThan(digits, MinDigits);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(digits, MaxDigits);
		return string.Create(digits, pick, static (span, draw) =>
		{
			for (int index = 0; index < span.Length; index++)
			{
				span[index] = CharacterSets.Digits[draw(CharacterSets.Digits.Length)];
			}
		});
	}
}
