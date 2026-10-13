namespace Moka.Passwords.Generation;

/// <summary>The kinds of character a password is drawn from. No character belongs to two of them.</summary>
public enum CharacterKind
{
	Lowercase,
	Uppercase,
	Digit,
	Symbol,
}
