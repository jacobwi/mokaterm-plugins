using System.Globalization;
using System.Text;

namespace Moka.Kubernetes.Server;

/// <summary>
/// One line of a log as <c>kubectl logs --timestamps</c> prints it: an RFC 3339 timestamp, a space, then the line the
/// container wrote. A line with no timestamp at the front is kept whole with no time, exactly as it arrived.
/// </summary>
public sealed record PodLogLine(string Raw, DateTimeOffset? Time, string Text);

/// <summary>
/// Reads log lines and the follow poll's bookkeeping around them. kubectl prints timestamps with up to nine
/// fractional digits, which .NET's own parser refuses after seven, so the reader trims the fraction before asking;
/// the follow poll asks for what is new with <c>--since-time</c>, which kubectl reads to whole seconds, so the
/// boundary is the newest line's second plus one, rounded down to stay on the safe side.
/// </summary>
public static class PodLogs
{
	/// <summary>Reads one line of a timestamped log.</summary>
	public static PodLogLine ReadLine(string raw)
	{
		ArgumentNullException.ThrowIfNull(raw);
		int space = raw.IndexOf(' ');
		if (space > 0 && TryReadTime(raw[..space], out DateTimeOffset time))
		{
			return new PodLogLine(raw, time, space + 1 < raw.Length ? raw[(space + 1)..] : "");
		}

		return new PodLogLine(raw, null, raw);
	}

	/// <summary>An RFC 3339 timestamp of whole seconds for <c>--since-time</c>: the second after the newest line, floored.</summary>
	public static string SinceTime(DateTimeOffset newest)
	{
		DateTimeOffset next = newest.ToUniversalTime().AddSeconds(1);
		DateTimeOffset floored = new(next.Year, next.Month, next.Day, next.Hour, next.Minute, next.Second, TimeSpan.Zero);
		return floored.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// Reads an RFC 3339 timestamp the way kubectl writes it, with any fraction up to nine digits; anything else
	/// reads as no timestamp rather than as a failure.
	/// </summary>
	internal static bool TryReadTime(string word, out DateTimeOffset time)
	{
		time = default;
		if (word.Length < "2026-01-01T00:00:00Z".Length)
		{
			return false;
		}

		string parsed = word;
		int dot = parsed.IndexOf('.');
		if (dot > 0)
		{
			// Nine fractional digits and a zone marker after them: keep seven digits and the marker.
			int zone = dot + 1;
			while (zone < parsed.Length && char.IsAsciiDigit(parsed[zone]))
			{
				zone++;
			}

			int digits = Math.Min(7, zone - dot - 1);
			parsed = string.Concat(parsed.AsSpan(0, dot + 1), parsed.AsSpan(dot + 1, digits), parsed.AsSpan(zone));
		}

		return DateTimeOffset.TryParse(parsed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time);
	}
}

/// <summary>
/// The lines of one log, from the tail fetch and the follow polls after it: parsed, kept in a ring of at most
/// <see cref="Capacity"/> lines with the oldest dropped first and counted, and de-duplicated across polls, because a
/// poll that re-delivers a line already kept must not list it twice. Nothing is queued for the view: it takes a
/// <see cref="Snapshot"/> when <see cref="Version"/> moved.
/// </summary>
public sealed class LogLines
{
	/// <summary>How many lines the ring holds: the tail asks for 500 and a followed log grows past them.</summary>
	public const int Capacity = 2000;

	/// <summary>The longest line kept as one line, so one runaway dump cannot eat the ring.</summary>
	public const int MaxLineCharacters = 2048;

	private readonly Lock _lock = new();
	private readonly Queue<PodLogLine> _lines = new();
	private readonly HashSet<string> _kept = new(StringComparer.Ordinal);
	private PodLogLine? _newest;
	private long _dropped;
	private long _total;
	private long _skipped;
	private int _version;

	/// <summary>Bumped by every change, so a view can tell whether to take a new snapshot.</summary>
	public int Version
	{
		get
		{
			lock (_lock)
			{
				return _version;
			}
		}
	}

	/// <summary>How many of the oldest lines were dropped to keep the rest.</summary>
	public long Dropped
	{
		get
		{
			lock (_lock)
			{
				return _dropped;
			}
		}
	}

	/// <summary>Every line the log has produced, dropped ones counted, which is the honest size of a log.</summary>
	public long Total
	{
		get
		{
			lock (_lock)
			{
				return _total;
			}
		}
	}

	/// <summary>How many lines a poll brought again and the ring did not list twice.</summary>
	public long Skipped
	{
		get
		{
			lock (_lock)
			{
				return _skipped;
			}
		}
	}

	/// <summary>How many lines the ring holds right now.</summary>
	public int Count
	{
		get
		{
			lock (_lock)
			{
				return _lines.Count;
			}
		}
	}

	/// <summary>The newest line's timestamp, for the follow poll's <c>--since-time</c>, or null when nothing is kept.</summary>
	public DateTimeOffset? Newest()
	{
		lock (_lock)
		{
			return _newest?.Time;
		}
	}

	/// <summary>Adds a block of log text as it came from one fetch: whole lines only, parsed, de-duplicated.</summary>
	public void Append(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		if (text.Length == 0)
		{
			return;
		}

		lock (_lock)
		{
			int start = 0;
			while (start < text.Length)
			{
				int end = text.IndexOf('\n', start);
				string raw = (end < 0 ? text[start..] : text[start..end]).TrimEnd('\r');
				if (raw.Length > 0)
				{
					Take(raw.Length > MaxLineCharacters ? raw[..MaxLineCharacters] : raw);
				}

				start = end < 0 ? text.Length : end + 1;
			}

			_version++;
		}
	}

	/// <summary>The kept lines, oldest first.</summary>
	public IReadOnlyList<PodLogLine> Snapshot()
	{
		lock (_lock)
		{
			return [.. _lines];
		}
	}

	/// <summary>Every kept line as it arrived, oldest first, for a copy of the log.</summary>
	public string AllText()
	{
		lock (_lock)
		{
			StringBuilder builder = new();
			foreach (PodLogLine line in _lines)
			{
				builder.AppendLine(line.Raw);
			}

			return builder.ToString();
		}
	}

	/// <summary>Everything the ring held, for a fetch that starts over.</summary>
	public void Clear()
	{
		lock (_lock)
		{
			_lines.Clear();
			_kept.Clear();
			_newest = null;
			_dropped = 0;
			_total = 0;
			_skipped = 0;
			_version++;
		}
	}

	private void Take(string raw)
	{
		if (!_kept.Add(raw))
		{
			_skipped++;
			return;
		}

		PodLogLine line = PodLogs.ReadLine(raw);
		_lines.Enqueue(line);
		_newest = line;
		_total++;
		while (_lines.Count > Capacity)
		{
			PodLogLine dropped = _lines.Dequeue();
			_kept.Remove(dropped.Raw);
			_dropped++;
		}
	}
}
