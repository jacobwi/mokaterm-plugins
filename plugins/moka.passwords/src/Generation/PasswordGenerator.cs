using System.Security.Cryptography;

namespace Moka.Passwords.Generation;

/// <summary>
/// Draws passwords uniformly from every string the options allow. "At least one of each kind" is met by drawing the
/// whole password and drawing again when a kind is missing, so each allowed password stays exactly as likely as any
/// other; forcing a character of each kind into place would favour the smaller kinds.
/// </summary>
public static class PasswordGenerator
{
	// The hardest rule the options allow (8 characters, every kind, a set of one symbol) still passes roughly one draw
	// in twelve. This many misses in a row does not happen; it only stops a broken rule from looping for ever.
	private const int MaxDraws = 100_000;

	public static string Generate(PasswordOptions options) => Generate(options, RandomNumberGenerator.GetInt32);

	/// <summary>Draws with <paramref name="pick"/>, which returns a whole number from 0 up to, not including, its argument.</summary>
	internal static string Generate(PasswordOptions options, Func<int, int> pick)
	{
		ArgumentNullException.ThrowIfNull(pick);
		PasswordAlphabet alphabet = PasswordAlphabet.For(options);
		char[] buffer = new char[options.Length];
		try
		{
			for (int draw = 0; draw < MaxDraws; draw++)
			{
				for (int index = 0; index < buffer.Length; index++)
				{
					buffer[index] = alphabet.Characters[pick(alphabet.Size)];
				}

				if (!options.RequireEachKind || alphabet.CoversEveryClass(buffer))
				{
					return new string(buffer);
				}
			}

			throw new InvalidOperationException("No draw held every kind of character.");
		}
		finally
		{
			// The string handed back cannot be wiped, but the rejected draws and this copy can.
			Array.Clear(buffer);
		}
	}
}
