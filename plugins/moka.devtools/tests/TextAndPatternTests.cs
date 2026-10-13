using Moka.DevTools.Components;
using Moka.DevTools.Patterns;
using Moka.DevTools.Text;

namespace Moka.DevTools.Tests;

public sealed class TextAndPatternTests
{
	[Fact]
	public void Position_CountsLinesAndCharacters()
	{
		string text = "ab\r\ncd\n" + char.ConvertFromUtf32(0x1F600) + "x\ry";

		Assert.Equal(new TextPosition(1, 1), TextPosition.Of(text, 0));
		Assert.Equal(new TextPosition(1, 3), TextPosition.Of(text, 2));
		Assert.Equal(new TextPosition(2, 2), TextPosition.Of(text, 5));
		Assert.Equal(new TextPosition(3, 2), TextPosition.Of(text, 9));
		Assert.Equal(new TextPosition(4, 1), TextPosition.Of(text, 11));
		Assert.Equal("line 2, column 2", TextPosition.Of(text, 5).ToString());
	}

	[Fact]
	public void Relative_SaysHowFarInWords()
	{
		DateTimeOffset now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

		Assert.Equal("3 hours ago", RelativeTime.Describe(now.AddHours(-3), now));
		Assert.Equal("in 2 days", RelativeTime.Describe(now.AddDays(2.5), now));
		Assert.Equal("just now", RelativeTime.Describe(now.AddMilliseconds(400), now));
		Assert.Equal("in 1 minute", RelativeTime.Describe(now.AddSeconds(90), now));
		Assert.Equal("47 hours ago", RelativeTime.Describe(now.AddHours(-47), now));
		Assert.Equal("3 months ago", RelativeTime.Describe(now.AddDays(-100), now));
		Assert.Equal("in 8 years", RelativeTime.Describe(now.AddYears(8), now));
	}

	[Fact]
	public void Dump_LinesUpOffsetsBytesAndText()
	{
		byte[] bytes = [.. "Hello, world!\n\0\x7f"u8.ToArray(), 0xC3];

		string[] lines = HexText.Dump(bytes).Split((char)10);

		Assert.Equal("00000000  48 65 6c 6c 6f 2c 20 77  6f 72 6c 64 21 0a 00 7f  |Hello, world!...|", lines[0]);
		Assert.StartsWith("00000010  c3 ", lines[1], StringComparison.Ordinal);
		Assert.EndsWith(" |.|", lines[1], StringComparison.Ordinal);
		Assert.Equal(lines[0].IndexOf('|', StringComparison.Ordinal), lines[1].IndexOf('|', StringComparison.Ordinal));
		Assert.Equal("", lines[2]);
	}

	[Fact]
	public void Strict_RefusesBytesThatAreNotUtf8()
	{
		Assert.True(StrictUtf8.TryDecode("é"u8, out string? text));
		Assert.Equal("é", text);
		Assert.False(StrictUtf8.TryDecode([0xC3, 0x28], out _));
		Assert.Throws<FormatException>(() => StrictUtf8.Encode("a" + (char)0xDC00));
	}

	[Fact]
	public void Highlight_CutsTheTextIntoRuns()
	{
		RegexMatchSpan[] matches = [new(1, 3, null), new(3, 3, null), new(5, 6, null)];

		IReadOnlyList<HighlightSegment> segments = RegexHighlight.Segments("abcdefg", matches);

		Assert.Equal(
			[new HighlightSegment("a", -1), new HighlightSegment("bc", 0), new HighlightSegment("", 1), new HighlightSegment("de", -1), new HighlightSegment("f", 2), new HighlightSegment("g", -1)],
			segments);
	}

	[Fact]
	public void Highlight_SkipsOffsetsThatDoNotFitAndStopsAtTheLimit()
	{
		RegexMatchSpan[] matches = [new(2, 4, null), new(3, 5, null), new(6, 99, null), new(5, 7, null)];

		Assert.Equal(["ab", "cd", "e", "fg"], RegexHighlight.Segments("abcdefg", matches).Select(segment => segment.Text));
		Assert.Equal(["ab", "c"], RegexHighlight.Segments("abcdefg", matches, limit: 3).Select(segment => segment.Text));
		Assert.Null(RegexHighlight.Slice("abc", 2, 9));
		Assert.Equal("bc", RegexHighlight.Slice("abc", 1, 3));
	}

	[Fact]
	public void Records_HoldingSecretsPrintSizesOnly()
	{
		TextValue value = new() { Text = "hunter2" };
		RegexRequest request = new("p(a)ss", "g", "token=hunter2", 250, 10, 10);

		Assert.DoesNotContain("hunter2", value.ToString(), StringComparison.Ordinal);
		Assert.DoesNotContain("hunter2", request.ToString(), StringComparison.Ordinal);
		Assert.DoesNotContain("p(a)ss", request.ToString(), StringComparison.Ordinal);
	}
}
