namespace Moka.Passwords.Generation;

/// <summary>One kind of character and the characters it offers, each once, with look-alikes already left out if asked.</summary>
public sealed record CharacterClass(CharacterKind Kind, string Characters);
