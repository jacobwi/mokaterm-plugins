using System.Globalization;

namespace Moka.Kubernetes.Server;

/// <summary>
/// Orders the columns kubectl prints as words, so a table sorts them by what they say rather than letter by letter:
/// an age such as <c>5d2h</c> or <c>8m</c>, a count of ready pods such as <c>2/3</c>, and a restart count such as
/// <c>5 (2m ago)</c>. Anything that does not read sorts after everything that does.
/// </summary>
public static class KubectlColumns
{
	/// <summary>An age in seconds, from kubectl's short form (<c>45s</c>, <c>8m</c>, <c>5d2h</c>, <c>2y40d</c>), or null.</summary>
	public static long? AgeSeconds(string age)
	{
		ArgumentNullException.ThrowIfNull(age);
		if (age.Length == 0)
		{
			return null;
		}

		long total = 0;
		long number = 0;
		bool digits = false;
		foreach (char c in age)
		{
			if (char.IsAsciiDigit(c))
			{
				number = (number * 10) + (c - '0');
				digits = true;
				continue;
			}

			long unit = c switch
			{
				's' => 1,
				'm' => 60,
				'h' => 3600,
				'd' => 86_400,
				'y' => 365 * 86_400,
				_ => 0,
			};
			if (unit == 0 || !digits)
			{
				return null;
			}

			total += number * unit;
			number = 0;
			digits = false;
		}

		return digits ? null : total;
	}

	/// <summary>The share of a <c>ready/wanted</c> count, or null when it does not read as one.</summary>
	public static double? ReadyShare(string ready) =>
		ReadyCounts(ready) is { Wanted: > 0 } counts ? (double)counts.Ready / counts.Wanted : null;

	/// <summary>The two numbers of a <c>ready/wanted</c> count, or null when it does not read as one.</summary>
	public static (int Ready, int Wanted)? ReadyCounts(string ready)
	{
		ArgumentNullException.ThrowIfNull(ready);
		return ready.Split('/') is [string have, string want] &&
			int.TryParse(have, NumberStyles.None, CultureInfo.InvariantCulture, out int haveCount) &&
			int.TryParse(want, NumberStyles.None, CultureInfo.InvariantCulture, out int wantCount)
				? (haveCount, wantCount)
				: null;
	}

	/// <summary>The count at the front of a restart column, which may carry <c>(2m ago)</c> after it, or null.</summary>
	public static int? Restarts(string restarts)
	{
		ArgumentNullException.ThrowIfNull(restarts);
		int end = restarts.IndexOf(' ', StringComparison.Ordinal);
		return int.TryParse(end < 0 ? restarts : restarts[..end], NumberStyles.None, CultureInfo.InvariantCulture, out int count) ? count : null;
	}

	/// <summary>Compares two read values, the ones that did not read after the ones that did.</summary>
	public static int Compare<T>(T? left, T? right)
		where T : struct, IComparable<T> => (left, right) switch
		{
			({ } a, { } b) => a.CompareTo(b),
			(null, null) => 0,
			(null, _) => 1,
			_ => -1,
		};
}
