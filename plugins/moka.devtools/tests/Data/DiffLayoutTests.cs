using Moka.DevTools.Data;

namespace Moka.DevTools.Tests.Data;

public sealed class DiffLayoutTests
{
	private static readonly string Before = string.Join('\n', Enumerable.Range(1, 30).Select(number => $"line {number}"));

	[Fact]
	public void Hunks_SplitAtFoldedRunsWithDiffHeaders()
	{
		string after = Before.Replace("line 5\n", "line five\n", StringComparison.Ordinal).Replace("line 25\n", "", StringComparison.Ordinal);

		DiffLayout layout = DiffLayout.Of(LineDiff.Compare(Before, after, TestContext.Current.CancellationToken), 3000);

		Assert.Equal(2, layout.Hunks.Count);
		Assert.Equal(1, layout.Hunks[0].SkippedBefore);
		Assert.Equal("@@ -2,7 +2,7 @@", layout.Hunks[0].Header);
		Assert.Equal(13, layout.Hunks[1].SkippedBefore);
		Assert.Equal("@@ -22,7 +22,6 @@", layout.Hunks[1].Header);
		Assert.Equal(2, layout.SkippedAfter);
		Assert.Equal(0, layout.RowsLeftOut);
	}

	[Fact]
	public void Hunk_OfAdditionsOnly_NamesTheLineBefore()
	{
		DiffLayout layout = DiffLayout.Of(LineDiff.Compare("a\nb", "a\nb\nc", TestContext.Current.CancellationToken), 3000);

		DiffHunk hunk = Assert.Single(layout.Hunks);
		Assert.Equal("@@ -1,2 +1,3 @@", hunk.Header);
		Assert.Equal("@@ -0,0 +1,1 @@", Assert.Single(DiffLayout.Of(LineDiff.Compare("", "new", TestContext.Current.CancellationToken), 3000).Hunks).Header);
	}

	[Fact]
	public void Rows_PastTheLimit_AreCountedNotDrawn()
	{
		string after = string.Join('\n', Enumerable.Range(1, 30).Select(number => $"other {number}"));

		DiffLayout layout = DiffLayout.Of(LineDiff.Compare(Before, after, TestContext.Current.CancellationToken), 10);

		Assert.Equal(10, layout.Hunks.Sum(hunk => hunk.Lines.Count));
		Assert.Equal(50, layout.RowsLeftOut);
		Assert.Equal(0, layout.SkippedAfter);
	}
}
