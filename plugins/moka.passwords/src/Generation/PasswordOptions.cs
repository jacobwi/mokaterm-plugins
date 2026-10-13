namespace Moka.Passwords.Generation;

/// <summary>What a password is made of. <see cref="PasswordAlphabet.ProblemWith"/> says whether the options can be used.</summary>
public sealed record PasswordOptions
{
	/// <summary>
	/// The shortest password. It is also above the number of kinds there are, which is what lets "at least one of each"
	/// hold at every length offered.
	/// </summary>
	public const int MinLength = 8;

	public const int MaxLength = 128;

	public const int DefaultLength = 20;

	public int Length { get; init; } = DefaultLength;

	public bool Lowercase { get; init; } = true;

	public bool Uppercase { get; init; } = true;

	public bool Digits { get; init; } = true;

	public bool Symbols { get; init; } = true;

	/// <summary>The symbols to draw from while <see cref="Symbols"/> is on; repeats count once.</summary>
	public string SymbolSet { get; init; } = CharacterSets.DefaultSymbols;

	/// <summary>Leaves out <see cref="CharacterSets.LookAlikes"/>.</summary>
	public bool AvoidLookAlikes { get; init; }

	/// <summary>Every chosen kind appears at least once.</summary>
	public bool RequireEachKind { get; init; } = true;

	/// <summary>Whether the kind is chosen.</summary>
	public bool Has(CharacterKind kind) => kind switch
	{
		CharacterKind.Lowercase => Lowercase,
		CharacterKind.Uppercase => Uppercase,
		CharacterKind.Digit => Digits,
		_ => Symbols,
	};

	/// <summary>A copy with the kind turned on or off.</summary>
	public PasswordOptions With(CharacterKind kind, bool on) => kind switch
	{
		CharacterKind.Lowercase => this with { Lowercase = on },
		CharacterKind.Uppercase => this with { Uppercase = on },
		CharacterKind.Digit => this with { Digits = on },
		_ => this with { Symbols = on },
	};

	/// <summary>How many kinds are chosen.</summary>
	public int KindCount => Enum.GetValues<CharacterKind>().Count(Has);
}
