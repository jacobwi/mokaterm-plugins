namespace Moka.Passwords.Generation;

/// <summary>The characters a password is drawn from, kind by kind, as the options describe them.</summary>
public sealed class PasswordAlphabet
{
	private PasswordAlphabet(IReadOnlyList<CharacterClass> classes)
	{
		Classes = classes;
		Characters = string.Concat(classes.Select(item => item.Characters));
	}

	/// <summary>The chosen kinds, none of them empty, no character in two of them.</summary>
	public IReadOnlyList<CharacterClass> Classes { get; }

	/// <summary>Every character on offer, each once.</summary>
	public string Characters { get; }

	public int Size => Characters.Length;

	/// <summary>Why the options cannot make a password, or null when they can.</summary>
	public static string? ProblemWith(PasswordOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		if (options.Length is < PasswordOptions.MinLength or > PasswordOptions.MaxLength)
		{
			return $"The length is {PasswordOptions.MinLength} to {PasswordOptions.MaxLength} characters.";
		}

		if (options.KindCount == 0)
		{
			return "Choose at least one kind of character.";
		}

		if (options.Symbols)
		{
			if (CharacterSets.SymbolSetProblem(options.SymbolSet) is { } symbolProblem)
			{
				return symbolProblem;
			}

			if (Kept(options.SymbolSet, options.AvoidLookAlikes).Length == 0)
			{
				return "Every symbol in the set is a look-alike, so none is left.";
			}
		}

		// MinLength is above the number of kinds, so this only guards a caller that goes around it.
		if (options.RequireEachKind && options.Length < options.KindCount)
		{
			return "The password is shorter than the number of kinds it must hold.";
		}

		return null;
	}

	/// <summary>The alphabet for options that <see cref="ProblemWith"/> accepts.</summary>
	/// <exception cref="ArgumentException">The options cannot make a password.</exception>
	public static PasswordAlphabet For(PasswordOptions options)
	{
		if (ProblemWith(options) is { } problem)
		{
			throw new ArgumentException(problem, nameof(options));
		}

		List<CharacterClass> classes = [];
		foreach (CharacterKind kind in Enum.GetValues<CharacterKind>())
		{
			if (options.Has(kind))
			{
				string source = kind switch
				{
					CharacterKind.Lowercase => CharacterSets.Lowercase,
					CharacterKind.Uppercase => CharacterSets.Uppercase,
					CharacterKind.Digit => CharacterSets.Digits,
					_ => options.SymbolSet,
				};

				classes.Add(new CharacterClass(kind, Kept(source, options.AvoidLookAlikes)));
			}
		}

		return new PasswordAlphabet(classes);
	}

	/// <summary>Whether every kind appears in the text at least once.</summary>
	public bool CoversEveryClass(ReadOnlySpan<char> text)
	{
		foreach (CharacterClass item in Classes)
		{
			if (text.IndexOfAny(item.Characters) < 0)
			{
				return false;
			}
		}

		return true;
	}

	private static string Kept(string characters, bool avoidLookAlikes)
	{
		string distinct = CharacterSets.Distinct(characters);
		return avoidLookAlikes ? CharacterSets.WithoutLookAlikes(distinct) : distinct;
	}
}
