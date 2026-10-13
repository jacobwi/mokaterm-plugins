namespace Moka.Passwords.Generation;

/// <summary>What goes between the words of a passphrase. Stored by name in the settings.</summary>
public enum PassphraseSeparator
{
	Space,
	Dash,
	Dot,
	Underscore,
	None,
	Custom,
}
