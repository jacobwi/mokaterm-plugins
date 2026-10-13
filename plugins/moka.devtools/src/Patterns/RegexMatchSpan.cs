namespace Moka.DevTools.Patterns;

/// <summary>One match as UTF-16 offsets into the sample, which are the same offsets in JavaScript and .NET.</summary>
public sealed record RegexMatchSpan(int Index, int End, IReadOnlyList<RegexGroupSpan>? Groups)
{
	public int Length => End - Index;
}
