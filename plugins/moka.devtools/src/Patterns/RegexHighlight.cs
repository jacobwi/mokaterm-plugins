namespace Moka.DevTools.Patterns;

/// <summary>Cuts the sample into plain runs and matches, for the view to draw. Offsets that do not fit the text are skipped.</summary>
public static class RegexHighlight
{
	/// <summary>The runs of the first <paramref name="limit"/> characters; a match that crosses the limit is cut there.</summary>
	public static IReadOnlyList<HighlightSegment> Segments(string text, IReadOnlyList<RegexMatchSpan> matches, int limit = int.MaxValue)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentNullException.ThrowIfNull(matches);
		int end = Math.Min(text.Length, limit);
		List<HighlightSegment> segments = [];
		int at = 0;
		for (int number = 0; number < matches.Count && at < end; number++)
		{
			RegexMatchSpan match = matches[number];
			if (match.Index < at || match.End < match.Index || match.End > text.Length || match.Index > end)
			{
				continue;
			}

			if (match.Index > at)
			{
				segments.Add(new HighlightSegment(text[at..match.Index], -1));
			}

			int matchEnd = Math.Min(match.End, end);
			segments.Add(new HighlightSegment(text[match.Index..matchEnd], number));
			at = matchEnd;
		}

		if (at < end)
		{
			segments.Add(new HighlightSegment(text[at..end], -1));
		}

		return segments;
	}

	/// <summary>The text a span covers, or null when the offsets do not fit it.</summary>
	public static string? Slice(string text, int start, int end) =>
		start >= 0 && end >= start && end <= text.Length ? text[start..end] : null;
}
