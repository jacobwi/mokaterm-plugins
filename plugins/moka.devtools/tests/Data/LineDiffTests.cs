using Moka.DevTools.Data;

namespace Moka.DevTools.Tests.Data;

public sealed class LineDiffTests
{
	[Fact]
	public void Identical_HasNoRows()
	{
		DiffResult result = LineDiff.Compare("a\nb\n", "a\r\nb", TestContext.Current.CancellationToken);

		Assert.True(result.Identical);
		Assert.Empty(result.Lines);
	}

	[Fact]
	public void Change_KeepsThreeLinesOfContextAndFoldsTheRest()
	{
		string before = string.Join('\n', Enumerable.Range(1, 20).Select(number => $"line {number}"));
		string after = before.Replace("line 10", "line ten", StringComparison.Ordinal);

		DiffResult result = LineDiff.Compare(before, after, TestContext.Current.CancellationToken);

		Assert.Equal(1, result.Added);
		Assert.Equal(1, result.Removed);
		Assert.Equal(
			[DiffKind.Skipped, DiffKind.Same, DiffKind.Same, DiffKind.Same, DiffKind.Removed, DiffKind.Added, DiffKind.Same, DiffKind.Same, DiffKind.Same, DiffKind.Skipped],
			result.Lines.Select(line => line.Kind));
		Assert.Equal(6, result.Lines[0].Count);
		Assert.Equal(new DiffLine(DiffKind.Removed, 10, 0, "line 10"), result.Lines[4]);
		Assert.Equal(new DiffLine(DiffKind.Added, 0, 10, "line ten"), result.Lines[5]);
		Assert.Equal(7, result.Lines[^1].Count);
		Assert.Equal(14, result.Lines[^1].OldNumber);
	}

	[Fact]
	public void Ends_AreComparedToo()
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		DiffResult result = LineDiff.Compare("x\na\nb", "a\nb\ny", token);

		Assert.Equal([DiffKind.Removed, DiffKind.Same, DiffKind.Same, DiffKind.Added], result.Lines.Select(line => line.Kind));
		Assert.Equal([DiffKind.Added], LineDiff.Compare("", "new", token).Lines.Select(line => line.Kind));
		Assert.Equal([DiffKind.Removed], LineDiff.Compare("old", "", token).Lines.Select(line => line.Kind));
	}

	[Fact]
	public void Search_FindsTheShortestEdit()
	{
		Random random = new(11);
		for (int round = 0; round < 300; round++)
		{
			string[] before = Lines(random);
			string[] after = Lines(random);

			DiffResult result = LineDiff.Compare(string.Join('\n', before), string.Join('\n', after), int.MaxValue, LineDiff.MaxChanges, LineDiff.MaxSteps, TestContext.Current.CancellationToken);

			// Rebuilding either side from the rows proves the rows are a real edit; the count proves it is a shortest one.
			Assert.Equal(before, result.Lines.Where(line => line.Kind is DiffKind.Same or DiffKind.Removed).Select(line => line.Text));
			Assert.Equal(after, result.Lines.Where(line => line.Kind is DiffKind.Same or DiffKind.Added).Select(line => line.Text));
			Assert.Equal(before.Length + after.Length - (2 * CommonLength(before, after)), result.Added + result.Removed);
		}
	}

	[Fact]
	public void Caps_GiveUpInsteadOfWorkingForever()
	{
		string before = string.Join('\n', Enumerable.Range(0, 300).Select(number => $"a{number}"));
		string after = string.Join('\n', Enumerable.Range(0, 300).Select(number => $"b{number}"));

		Assert.True(LineDiff.Compare(before, after, LineDiff.Context, maxChanges: 100, LineDiff.MaxSteps, TestContext.Current.CancellationToken).TooDifferent);
		Assert.True(LineDiff.Compare(before + "\nsame", "same\n" + after, LineDiff.Context, LineDiff.MaxChanges, maxSteps: 1000, TestContext.Current.CancellationToken).TooDifferent);
		Assert.False(LineDiff.Compare(before, after, TestContext.Current.CancellationToken).TooDifferent);
	}

	[Fact]
	public void Defaults_MatchMokatermsEditor()
	{
		Assert.Equal(3, LineDiff.Context);
		Assert.Equal(2000, LineDiff.MaxChanges);
		Assert.Equal(20_000_000, LineDiff.MaxSteps);
	}

	[Fact]
	public void Cancel_StopsTheSearch()
	{
		using CancellationTokenSource cancelled = new();
		cancelled.Cancel();

		Assert.Throws<OperationCanceledException>(() => LineDiff.Compare("a\nb", "b\nc", cancelled.Token));
	}

	private static string[] Lines(Random random) =>
		[.. Enumerable.Range(0, random.Next(0, 12)).Select(_ => ((char)('a' + random.Next(0, 4))).ToString())];

	private static int CommonLength(string[] left, string[] right)
	{
		int[,] table = new int[left.Length + 1, right.Length + 1];
		for (int i = left.Length - 1; i >= 0; i--)
		{
			for (int j = right.Length - 1; j >= 0; j--)
			{
				table[i, j] = left[i] == right[j] ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);
			}
		}

		return table[0, 0];
	}
}
