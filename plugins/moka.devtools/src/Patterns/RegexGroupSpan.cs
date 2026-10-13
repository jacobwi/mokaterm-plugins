namespace Moka.DevTools.Patterns;

/// <summary>One capture group of one match, as offsets into the sample; -1 when the group took no part in the match.</summary>
public sealed record RegexGroupSpan(int Number, string? Name, int Start, int End)
{
	public bool Matched => Start >= 0;
}
