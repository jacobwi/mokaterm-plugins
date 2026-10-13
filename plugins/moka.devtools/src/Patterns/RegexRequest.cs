using System.Text;

namespace Moka.DevTools.Patterns;

/// <summary>One run handed to the page: the pattern, its flags, the sample and the limits.</summary>
public sealed record RegexRequest(string Pattern, string Flags, string Text, int TimeoutMs, int MaxMatches, int MaxGroups)
{
	/// <summary>Time one run may take in the worker before the page stops it.</summary>
	public const int DefaultTimeoutMs = 250;

	/// <summary>The most matches sent back; past it the result says it was cut.</summary>
	public const int DefaultMaxMatches = 1000;

	/// <summary>The most group spans sent back over all matches, which keeps the reply under the web host's message limit.</summary>
	public const int DefaultMaxGroups = 20_000;

	// A sample may be a log with tokens in it; the generated ToString prints sizes only.
	private bool PrintMembers(StringBuilder builder)
	{
		builder.Append("Pattern = ").Append(Pattern.Length).Append(" chars, Flags = ").Append(Flags)
			.Append(", Text = ").Append(Text.Length).Append(" chars");
		return true;
	}
}
