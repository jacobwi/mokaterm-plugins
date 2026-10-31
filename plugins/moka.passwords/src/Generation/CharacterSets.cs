using System.Text;

namespace Moka.Passwords.Generation;

/// <summary>The characters each kind offers, the look-alikes, and the one rule for a symbol set someone typed.</summary>
public static class CharacterSets
{
	public const string Lowercase = "abcdefghijklmnopqrstuvwxyz";

	public const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

	public const string Digits = "0123456789";

	/// <summary>
	/// ASCII punctuation without the quotes, the backtick and the backslash, which break out of shell and config file
	/// quoting when a password is pasted into one.
	/// </summary>
	public const string DefaultSymbols = "!#$%&()*+,-./:;<=>?@[]^_{|}~";

	/// <summary>Characters easily read as one another in many fonts: capital I, small l, one and bar; capital O, zero and small o.</summary>
	public const string LookAlikes = "Il1|O0o";

	/// <summary>The longest symbol set taken as typed. There are only 32 ASCII symbols, so this leaves room for repeats.</summary>
	public const int MaxSymbolSetLength = 64;

	/// <summary>
	/// The longest excluded list taken as typed. Only the 94 printable ASCII characters can be excluded at all, so this
	/// leaves room for repeats and for characters no kind offers.
	/// </summary>
	public const int MaxExcludedLength = 94;

	/// <summary>A symbol is printable ASCII that is neither a letter, a digit nor a space.</summary>
	public static bool IsSymbol(char c) => c is > ' ' and <= '~' && !char.IsAsciiLetterOrDigit(c);

	/// <summary>Why the text cannot be a symbol set, or null when it can.</summary>
	public static string? SymbolSetProblem(string? text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return "Type at least one symbol.";
		}

		if (text.Length > MaxSymbolSetLength)
		{
			return $"At most {MaxSymbolSetLength} characters.";
		}

		return text.All(IsSymbol) ? null : "Only ASCII symbols: no letters, digits, spaces or other characters.";
	}

	/// <summary>Why the text cannot be an excluded list, or null when it can. Empty excludes nothing, and any character may be on it.</summary>
	public static string? ExcludedProblem(string? text) => text is null || text.Length <= MaxExcludedLength
		? null
		: $"At most {MaxExcludedLength} characters.";

	/// <summary>Each character of the text once, in the order typed. Repeats would weigh a symbol more than the others.</summary>
	public static string Distinct(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		HashSet<char> seen = [];
		StringBuilder kept = new(text.Length);
		foreach (char c in text)
		{
			if (seen.Add(c))
			{
				kept.Append(c);
			}
		}

		return kept.ToString();
	}

	/// <summary>The text without the look-alikes.</summary>
	public static string WithoutLookAlikes(string text) => Without(text, LookAlikes);

	/// <summary>The text without every character of <paramref name="remove"/>.</summary>
	public static string Without(string text, string remove)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(remove);
		return remove.Length == 0 ? text : string.Concat(text.Where(c => !remove.Contains(c, StringComparison.Ordinal)));
	}
}
