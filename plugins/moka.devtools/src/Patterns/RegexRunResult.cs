namespace Moka.DevTools.Patterns;

/// <summary>What one run in the page's worker came back with. Only offsets cross; the view reads the text from its own copy.</summary>
public sealed record RegexRunResult(
	string Status,
	string? Error,
	IReadOnlyList<RegexMatchSpan>? Matches,
	int Total,
	bool Truncated,
	double Elapsed,
	RegexTriggerCheck? Trigger)
{
	public static RegexRunResult Unavailable(string message) => new(RegexRunStatus.Unavailable, message, null, 0, false, 0, null);
}
