namespace Moka.DevTools.Patterns;

/// <summary>A run of the sample: plain text, or match number <see cref="Match"/> (from 0), which may be empty.</summary>
public sealed record HighlightSegment(string Text, int Match)
{
	public bool IsMatch => Match >= 0;
}
