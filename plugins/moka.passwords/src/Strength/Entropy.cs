using System.Numerics;
using Moka.Passwords.Generation;

namespace Moka.Passwords.Strength;

/// <summary>
/// Bits of entropy worked out from a generator's settings: log2 of how many different values it can make, each as
/// likely as any other. That is exact for what the generator makes and says nothing about a password someone typed.
/// </summary>
public static class Entropy
{
	/// <summary>What one word of the list adds: log2 of 7776, about 12.9 bits.</summary>
	public static double BitsPerWord { get; } = Math.Log2(WordList.Count);

	/// <summary>Every password the options allow is equally likely, so the bits are log2 of how many there are.</summary>
	/// <exception cref="ArgumentException">The options cannot make a password.</exception>
	public static double OfPassword(PasswordOptions options)
	{
		PasswordAlphabet alphabet = PasswordAlphabet.For(options);
		return BigInteger.Log(CountPasswords([.. alphabet.Classes.Select(item => item.Characters.Length)], options.Length, options.RequireEachKind), 2);
	}

	/// <summary>
	/// How many different passwords of the length the kinds make. With the rule, inclusion and exclusion over the kinds
	/// left out: every string, minus those missing one kind, plus those missing two, and so on. The kinds share no
	/// character, which is what makes the sum exact.
	/// </summary>
	internal static BigInteger CountPasswords(IReadOnlyList<int> classSizes, int length, bool requireEach)
	{
		ArgumentNullException.ThrowIfNull(classSizes);
		int total = classSizes.Sum();
		if (!requireEach)
		{
			return BigInteger.Pow(total, length);
		}

		BigInteger count = BigInteger.Zero;
		for (int leftOut = 0; leftOut < 1 << classSizes.Count; leftOut++)
		{
			int missing = 0;
			for (int index = 0; index < classSizes.Count; index++)
			{
				if ((leftOut & (1 << index)) != 0)
				{
					missing += classSizes[index];
				}
			}

			BigInteger term = BigInteger.Pow(total - missing, length);
			count += BitOperations.PopCount((uint)leftOut) % 2 == 0 ? term : -term;
		}

		return count;
	}

	/// <summary>
	/// The words, plus each extra's value times the word it follows. Capitals add nothing, since every word gets one.
	/// Exact while every set of choices spells a different passphrase; see <see cref="PassphraseOptions.BoundariesAmbiguous"/>
	/// for when it is an upper bound instead.
	/// </summary>
	/// <exception cref="ArgumentException">The options cannot make a passphrase.</exception>
	public static double OfPassphrase(PassphraseOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		if (options.Problem() is { } problem)
		{
			throw new ArgumentException(problem, nameof(options));
		}

		double bits = options.Words * BitsPerWord;
		if (options.AddDigit)
		{
			bits += Math.Log2(CharacterSets.Digits.Length * (double)options.Words);
		}

		if (options.AddSymbol)
		{
			bits += Math.Log2(CharacterSets.Distinct(options.SymbolSet).Length * (double)options.Words);
		}

		return bits;
	}

	/// <summary>Every digit is one of ten, independent of the others.</summary>
	public static double OfPin(int digits) => digits * Math.Log2(CharacterSets.Digits.Length);
}
