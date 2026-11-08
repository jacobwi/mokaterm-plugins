namespace Moka.DevTools.Data;

/// <summary>
/// A diff's rows cut into hunks at every run of unchanged lines it folded, up to a number of rows drawn: what a view
/// draws, the unchanged lines after the last hunk, and the rows past the limit that it leaves out.
/// </summary>
public sealed record DiffLayout(IReadOnlyList<DiffHunk> Hunks, int SkippedAfter, int RowsLeftOut)
{
	public static DiffLayout Of(DiffResult diff, int maxRows)
	{
		ArgumentNullException.ThrowIfNull(diff);
		List<DiffHunk> hunks = [];
		List<DiffLine> current = [];
		int skipped = 0;
		int lastOld = 0;
		int lastNew = 0;
		int drawn = 0;
		int leftOut = 0;
		foreach (DiffLine line in diff.Lines)
		{
			if (line.Kind == DiffKind.Skipped)
			{
				Close();
				skipped += line.Count;
				lastOld = line.OldNumber + line.Count - 1;
				lastNew = line.NewNumber + line.Count - 1;
				continue;
			}

			if (drawn >= maxRows)
			{
				leftOut++;
				continue;
			}

			current.Add(line);
			drawn++;
		}

		Close();
		return new DiffLayout(hunks, leftOut > 0 ? 0 : skipped, leftOut);

		void Close()
		{
			if (current.Count == 0)
			{
				return;
			}

			int oldCount = current.Count(line => line.Kind != DiffKind.Added);
			int newCount = current.Count(line => line.Kind != DiffKind.Removed);

			// Like diff, a side with no lines in the hunk names the line before it.
			int oldStart = current.FirstOrDefault(line => line.OldNumber > 0)?.OldNumber ?? lastOld;
			int newStart = current.FirstOrDefault(line => line.NewNumber > 0)?.NewNumber ?? lastNew;
			hunks.Add(new DiffHunk(skipped, oldStart, oldCount, newStart, newCount, [.. current]));
			lastOld = current.LastOrDefault(line => line.OldNumber > 0)?.OldNumber ?? lastOld;
			lastNew = current.LastOrDefault(line => line.NewNumber > 0)?.NewNumber ?? lastNew;
			current.Clear();
			skipped = 0;
		}
	}
}
