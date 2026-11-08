namespace Moka.Passwords.Tool;

// A class rather than a record: a record's generated ToString would print the value, and the value is a secret.
internal sealed class GeneratedRow(int number, string value)
{
	public int Number { get; } = number;

	public string Value { get; } = value;
}
