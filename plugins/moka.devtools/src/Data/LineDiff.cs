namespace Moka.DevTools.Data;

/// <summary>
/// A line diff by Myers' O(ND) method, the shortest edit script diff and git print, with <see cref="Context"/>
/// unchanged lines kept around each change. The search is capped the way Mokaterm's own editor caps it: past
/// <see cref="MaxChanges"/> changed lines, or <see cref="MaxSteps"/> steps of work, it gives up and says the texts are
/// too different to compare line by line, rather than holding the page for seconds.
/// </summary>
public static class LineDiff
{
	public const int Context = 3;

	/// <summary>The most changed lines (added plus removed) the search looks for.</summary>
	public const int MaxChanges = 2000;

	/// <summary>The most line comparisons the search makes before it gives up.</summary>
	public const long MaxSteps = 20_000_000;

	private enum Step
	{
		Keep,
		Remove,
		Add,
	}

	public static DiffResult Compare(string before, string after, CancellationToken cancellationToken = default) =>
		Compare(before, after, Context, MaxChanges, MaxSteps, cancellationToken);

	internal static DiffResult Compare(string before, string after, int context, int maxChanges, long maxSteps, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(before);
		ArgumentNullException.ThrowIfNull(after);
		string[] left = Lines(before);
		string[] right = Lines(after);

		// Most comparisons change a little in the middle, so what both share at the ends is set aside before searching.
		int prefix = 0;
		while (prefix < left.Length && prefix < right.Length && string.Equals(left[prefix], right[prefix], StringComparison.Ordinal))
		{
			prefix++;
		}

		int suffix = 0;
		while (suffix < left.Length - prefix && suffix < right.Length - prefix
			&& string.Equals(left[left.Length - 1 - suffix], right[right.Length - 1 - suffix], StringComparison.Ordinal))
		{
			suffix++;
		}

		List<Step>? middle = Search(left, right, prefix, suffix, maxChanges, maxSteps, cancellationToken);
		if (middle is null)
		{
			return DiffResult.TooFarApart;
		}

		List<Step> steps = new(prefix + middle.Count + suffix);
		steps.AddRange(Enumerable.Repeat(Step.Keep, prefix));
		steps.AddRange(middle);
		steps.AddRange(Enumerable.Repeat(Step.Keep, suffix));
		return Render(left, right, steps, context);
	}

	/// <summary>The lines of a text: CRLF, CR and LF all end one, and a final line break opens no empty line after it.</summary>
	internal static string[] Lines(string text)
	{
		if (text.Length == 0)
		{
			return [];
		}

		string[] lines = text.ReplaceLineEndings("\n").Split('\n');
		return lines[^1].Length == 0 ? lines[..^1] : lines;
	}

	/// <summary>The steps from the old middle to the new one, or null when the search ran past its caps.</summary>
	private static List<Step>? Search(string[] left, string[] right, int prefix, int suffix, int maxChanges, long maxSteps, CancellationToken cancellationToken)
	{
		int oldCount = left.Length - prefix - suffix;
		int newCount = right.Length - prefix - suffix;
		if (oldCount == 0 || newCount == 0)
		{
			List<Step> plain = [];
			plain.AddRange(Enumerable.Repeat(Step.Remove, oldCount));
			plain.AddRange(Enumerable.Repeat(Step.Add, newCount));
			return oldCount + newCount > maxChanges ? null : plain;
		}

		// Lines become numbers once, so the search compares integers.
		Dictionary<string, int> numbers = new(StringComparer.Ordinal);
		int[] a = Number(left, prefix, oldCount, numbers);
		int[] b = Number(right, prefix, newCount, numbers);

		int bound = Math.Min(oldCount + newCount, maxChanges);
		int center = bound + 1;
		int[] furthest = new int[(2 * bound) + 3];
		List<int[]> history = [];
		long work = 0;
		for (int changes = 0; changes <= bound; changes++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			for (int diagonal = -changes; diagonal <= changes; diagonal += 2)
			{
				// Either come down from the diagonal above (a line added) or across from the one below (a line removed),
				// whichever got further along the old text.
				bool down = diagonal == -changes
					|| (diagonal != changes && furthest[center + diagonal - 1] < furthest[center + diagonal + 1]);
				int x = down ? furthest[center + diagonal + 1] : furthest[center + diagonal - 1] + 1;
				int y = x - diagonal;
				while (x < oldCount && y < newCount && a[x] == b[y])
				{
					x++;
					y++;
					work++;
				}

				work++;
				furthest[center + diagonal] = x;
				if (x >= oldCount && y >= newCount)
				{
					history.Add(Snapshot(furthest, center, changes));
					return Backtrack(history, oldCount, newCount);
				}
			}

			if (work > maxSteps)
			{
				return null;
			}

			history.Add(Snapshot(furthest, center, changes));
		}

		return null;
	}

	// The diagonals one round reached, -changes to changes in steps of two: all that walking back needs from it.
	private static int[] Snapshot(int[] furthest, int center, int changes)
	{
		int[] row = new int[changes + 1];
		for (int index = 0; index <= changes; index++)
		{
			row[index] = furthest[center - changes + (2 * index)];
		}

		return row;
	}

	private static int Reached(int[] row, int changes, int diagonal) => row[(diagonal + changes) / 2];

	private static List<Step> Backtrack(List<int[]> history, int oldCount, int newCount)
	{
		List<Step> reversed = [];
		int x = oldCount;
		int y = newCount;
		for (int changes = history.Count - 1; changes > 0; changes--)
		{
			int[] previous = history[changes - 1];
			int diagonal = x - y;
			bool down = diagonal == -changes
				|| (diagonal != changes && Reached(previous, changes - 1, diagonal - 1) < Reached(previous, changes - 1, diagonal + 1));
			int fromDiagonal = down ? diagonal + 1 : diagonal - 1;
			int fromX = Reached(previous, changes - 1, fromDiagonal);
			int fromY = fromX - fromDiagonal;
			int startX = down ? fromX : fromX + 1;
			while (x > startX)
			{
				reversed.Add(Step.Keep);
				x--;
				y--;
			}

			reversed.Add(down ? Step.Add : Step.Remove);
			x = fromX;
			y = fromY;
		}

		while (x > 0 && y > 0)
		{
			reversed.Add(Step.Keep);
			x--;
			y--;
		}

		reversed.Reverse();
		return reversed;
	}

	private static int[] Number(string[] lines, int from, int count, Dictionary<string, int> numbers)
	{
		int[] result = new int[count];
		for (int index = 0; index < count; index++)
		{
			string line = lines[from + index];
			if (!numbers.TryGetValue(line, out int number))
			{
				number = numbers.Count;
				numbers[line] = number;
			}

			result[index] = number;
		}

		return result;
	}

	private static DiffResult Render(string[] left, string[] right, List<Step> steps, int context)
	{
		// Each step with its line numbers first, so the context pass can look ahead and behind.
		List<DiffLine> all = new(steps.Count);
		int oldLine = 0;
		int newLine = 0;
		int added = 0;
		int removed = 0;

		// Within one change, removals print before additions, as diff does.
		List<DiffLine> pendingAdds = [];
		foreach (Step step in steps)
		{
			switch (step)
			{
				case Step.Remove:
					all.Add(new DiffLine(DiffKind.Removed, oldLine + 1, 0, left[oldLine]));
					oldLine++;
					removed++;
					break;
				case Step.Add:
					pendingAdds.Add(new DiffLine(DiffKind.Added, 0, newLine + 1, right[newLine]));
					newLine++;
					added++;
					break;
				default:
					all.AddRange(pendingAdds);
					pendingAdds.Clear();
					all.Add(new DiffLine(DiffKind.Same, oldLine + 1, newLine + 1, left[oldLine]));
					oldLine++;
					newLine++;
					break;
			}
		}

		all.AddRange(pendingAdds);
		if (added == 0 && removed == 0)
		{
			return new DiffResult([], 0, 0, TooDifferent: false);
		}

		// Keep unchanged lines within `context` of a change; fold the rest of each run into one skipped row.
		bool[] keep = new bool[all.Count];
		for (int index = 0; index < all.Count; index++)
		{
			if (all[index].Kind != DiffKind.Same)
			{
				// In longs, so a context of int.MaxValue (keep everything) cannot wrap around.
				int first = (int)Math.Max(0L, (long)index - context);
				int last = (int)Math.Min(all.Count - 1L, (long)index + context);
				for (int near = first; near <= last; near++)
				{
					keep[near] = true;
				}
			}
		}

		List<DiffLine> rows = [];
		int at = 0;
		while (at < all.Count)
		{
			if (keep[at])
			{
				rows.Add(all[at]);
				at++;
				continue;
			}

			int start = at;
			while (at < all.Count && !keep[at])
			{
				at++;
			}

			rows.Add(new DiffLine(DiffKind.Skipped, all[start].OldNumber, all[start].NewNumber, "", at - start));
		}

		return new DiffResult(rows, added, removed, TooDifferent: false);
	}
}
