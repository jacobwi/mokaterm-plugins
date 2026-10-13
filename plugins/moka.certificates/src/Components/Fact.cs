namespace Moka.Certificates.Components;

/// <summary>One labeled fact in a details list: a value, or several (names), shown in mono when it is data.</summary>
public sealed record Fact(string Label, IReadOnlyList<string> Values, bool Mono = false, bool Copyable = false)
{
	public Fact(string label, string value, bool mono = false, bool copyable = false)
		: this(label, [value], mono, copyable)
	{
	}
}
