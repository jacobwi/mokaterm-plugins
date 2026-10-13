using System.Globalization;
using System.Text.RegularExpressions;

namespace Moka.DevTools.Dates;

/// <summary>
/// Reads a Unix time (seconds, milliseconds, microseconds or nanoseconds, guessed from its size unless given) or a date
/// written as ISO 8601, RFC 3339 or RFC 1123. A field out of range is refused by name rather than rolled over.
/// </summary>
public static partial class TimeParser
{
	private const long TicksPerSecond = TimeSpan.TicksPerSecond;

	private static readonly string[] Months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

	private static readonly string[] Days = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

	/// <summary>The unit a bare number most likely is: seconds until 1e11 (the year 5138), then each next unit per 1000.</summary>
	public static EpochUnit GuessUnit(decimal value)
	{
		decimal size = Math.Abs(Math.Truncate(value));
		return size switch
		{
			< 100_000_000_000m => EpochUnit.Seconds,
			< 100_000_000_000_000m => EpochUnit.Milliseconds,
			< 100_000_000_000_000_000m => EpochUnit.Microseconds,
			_ => EpochUnit.Nanoseconds,
		};
	}

	public static string UnitName(EpochUnit unit) => unit switch
	{
		EpochUnit.Seconds => "seconds",
		EpochUnit.Milliseconds => "milliseconds",
		EpochUnit.Microseconds => "microseconds",
		_ => "nanoseconds",
	};

	/// <summary>Reads <paramref name="text"/>; <paramref name="unit"/> forces the unit of a number, null guesses it.</summary>
	public static TimeReading Read(string text, EpochUnit? unit = null)
	{
		ArgumentNullException.ThrowIfNull(text);
		string value = text.Trim();
		if (value.Length == 0)
		{
			throw new FormatException("Type a Unix time or a date.");
		}

		if (EpochPattern().IsMatch(value))
		{
			return ReadEpoch(value, unit);
		}

		if (IsoPattern().Match(value) is { Success: true } iso)
		{
			return ReadIso(iso);
		}

		if (IsoBasicPattern().Match(value) is { Success: true } basic)
		{
			return ReadIso(basic);
		}

		if (RfcPattern().Match(value) is { Success: true } rfc)
		{
			return ReadRfc1123(rfc);
		}

		throw new FormatException("Not a Unix time, an ISO 8601 or RFC 3339 date (2024-05-01T12:00:00Z), or an RFC 1123 date (Wed, 01 May 2024 12:00:00 GMT).");
	}

	private static TimeReading ReadEpoch(string text, EpochUnit? forced)
	{
		string digits = text.Replace("_", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
		if (!decimal.TryParse(digits, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value))
		{
			throw new FormatException("That number is too long to be a time.");
		}

		EpochUnit unit = forced ?? GuessUnit(value);

		// Past this no unit lands inside the years 1 to 9999, and scaling it to ticks would overflow a decimal.
		if (Math.Abs(value) >= 1e21m)
		{
			throw new FormatException($"In {UnitName(unit)} that is outside the years 1 to 9999.");
		}

		decimal ticks = unit switch
		{
			EpochUnit.Seconds => value * TicksPerSecond,
			EpochUnit.Milliseconds => value * TimeSpan.TicksPerMillisecond,
			EpochUnit.Microseconds => value * 10,
			_ => value / 100,
		};

		decimal whole = Math.Floor(ticks);
		decimal first = DateTimeOffset.MinValue.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
		decimal last = DateTimeOffset.MaxValue.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
		if (whole < first || whole > last)
		{
			throw new FormatException($"In {UnitName(unit)} that is outside the years 1 to 9999.");
		}

		List<string> notes = [];
		if (whole != ticks)
		{
			notes.Add("The time is finer than 100 ns, the smallest step here, so the rest was dropped.");
		}

		int integerDigits = Math.Abs(Math.Truncate(value)).ToString(CultureInfo.InvariantCulture).Length;
		return new TimeReading
		{
			Instant = DateTimeOffset.UnixEpoch.AddTicks((long)whole),
			Source = $"Unix {UnitName(unit)}",
			Unit = unit,
			UnitReason = forced is null ? string.Create(CultureInfo.InvariantCulture, $"{integerDigits} digits") : null,
			WrittenOffset = TimeSpan.Zero,
			Notes = notes,
		};
	}

	private static TimeReading ReadIso(Match match)
	{
		List<string> notes = [];
		int year = Number(match, "y");
		int month = Number(match, "mo");
		int day = Number(match, "d");
		CheckDate(year, month, day);

		bool hasTime = match.Groups["h"].Success;
		int hour = hasTime ? Number(match, "h") : 0;
		int minute = hasTime ? Number(match, "mi") : 0;
		int second = match.Groups["s"].Success ? Number(match, "s") : 0;
		CheckTime(hour, minute, second);

		long fractionTicks = 0;
		if (match.Groups["f"].Success)
		{
			string fraction = match.Groups["f"].Value;
			if (fraction.Length > 7)
			{
				notes.Add("The fraction has more than 7 digits; past 100 ns it was dropped.");
				fraction = fraction[..7];
			}

			fractionTicks = long.Parse(fraction.PadRight(7, '0'), CultureInfo.InvariantCulture);
		}

		TimeSpan? offset = match.Groups["z"].Success ? Offset(match.Groups["z"].Value) : null;
		if (offset is null)
		{
			notes.Add(hasTime ? "No offset was given, so the time is read as UTC." : "No time was given, so this is midnight UTC.");
		}

		DateTimeOffset instant = Build(year, month, day, hour, minute, second, fractionTicks, offset ?? TimeSpan.Zero);
		bool rfc3339 = hasTime && match.Groups["s"].Success && offset is not null
			&& (match.Groups["z"].Value is "Z" or "z" || match.Groups["z"].Value.Contains(':', StringComparison.Ordinal))
			&& match.Value.Contains('-', StringComparison.Ordinal);
		return new TimeReading
		{
			Instant = instant,
			Source = rfc3339 ? "RFC 3339" : "ISO 8601",
			WrittenOffset = offset,
			Notes = notes,
		};
	}

	private static TimeReading ReadRfc1123(Match match)
	{
		List<string> notes = [];
		int year = Number(match, "y");
		int month = Array.IndexOf(Months, match.Groups["mon"].Value.ToLowerInvariant()) + 1;
		int day = Number(match, "d");
		CheckDate(year, month, day);
		int hour = Number(match, "h");
		int minute = Number(match, "mi");
		int second = match.Groups["s"].Success ? Number(match, "s") : 0;
		CheckTime(hour, minute, second);

		string zone = match.Groups["z"].Value;
		TimeSpan offset = zone.Length > 0 && zone[0] is '+' or '-' ? Offset(zone) : TimeSpan.Zero;
		DateTimeOffset instant = Build(year, month, day, hour, minute, second, 0, offset);
		if (match.Groups["dow"].Success)
		{
			int written = Array.IndexOf(Days, match.Groups["dow"].Value.ToLowerInvariant());
			DayOfWeek actual = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Unspecified).DayOfWeek;
			if (written != (int)actual)
			{
				notes.Add($"The text says {match.Groups["dow"].Value}, but that date was a {actual}. The date was used.");
			}
		}

		return new TimeReading { Instant = instant, Source = "RFC 1123", WrittenOffset = offset, Notes = notes };
	}

	private static DateTimeOffset Build(int year, int month, int day, int hour, int minute, int second, long fractionTicks, TimeSpan offset)
	{
		DateTime local = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified).AddTicks(fractionTicks);
		long utcTicks = local.Ticks - offset.Ticks;
		if (utcTicks < DateTimeOffset.MinValue.UtcTicks || utcTicks > DateTimeOffset.MaxValue.UtcTicks)
		{
			throw new FormatException("With its offset that falls outside the years 1 to 9999.");
		}

		return new DateTimeOffset(local, offset);
	}

	private static void CheckDate(int year, int month, int day)
	{
		if (year < 1)
		{
			throw new FormatException("The year 0 is not a year here; the first is 1.");
		}

		if (month is < 1 or > 12)
		{
			throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Month {month} does not exist."));
		}

		int days = DateTime.DaysInMonth(year, month);
		if (day < 1 || day > days)
		{
			throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Day {day} does not exist in {CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month)} {year}, which has {days}."));
		}
	}

	private static void CheckTime(int hour, int minute, int second)
	{
		if (hour > 23)
		{
			throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Hour {hour} does not exist; hours run 0 to 23."));
		}

		if (minute > 59)
		{
			throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Minute {minute} does not exist; minutes run 0 to 59."));
		}

		if (second == 60)
		{
			throw new FormatException("A leap second (60) cannot be shown here.");
		}

		if (second > 59)
		{
			throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"Second {second} does not exist; seconds run 0 to 59."));
		}
	}

	private static TimeSpan Offset(string text)
	{
		if (text is "Z" or "z")
		{
			return TimeSpan.Zero;
		}

		int sign = text[0] == '-' ? -1 : 1;
		string digits = text[1..].Replace(":", "", StringComparison.Ordinal);
		int hours = int.Parse(digits[..2], CultureInfo.InvariantCulture);
		int minutes = digits.Length >= 4 ? int.Parse(digits[2..4], CultureInfo.InvariantCulture) : 0;
		if (hours > 14 || minutes > 59 || (hours == 14 && minutes > 0))
		{
			throw new FormatException($"The offset {text} is not a real one; offsets run from -14:00 to +14:00.");
		}

		return sign * new TimeSpan(hours, minutes, 0);
	}

	private static int Number(Match match, string group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

	[GeneratedRegex(@"^[+-]?[0-9][0-9_ ]*(\.[0-9]+)?$", RegexOptions.CultureInvariant)]
	private static partial Regex EpochPattern();

	[GeneratedRegex(@"^(?<y>[0-9]{4})-(?<mo>[0-9]{2})-(?<d>[0-9]{2})(?:[Tt ](?<h>[0-9]{2}):(?<mi>[0-9]{2})(?::(?<s>[0-9]{2})(?:[.,](?<f>[0-9]+))?)?\s*(?<z>[Zz]|[+-][0-9]{2}(?::?[0-9]{2})?)?)?$", RegexOptions.CultureInvariant)]
	private static partial Regex IsoPattern();

	[GeneratedRegex(@"^(?<y>[0-9]{4})(?<mo>[0-9]{2})(?<d>[0-9]{2})[Tt](?<h>[0-9]{2})(?<mi>[0-9]{2})(?:(?<s>[0-9]{2})(?:[.,](?<f>[0-9]+))?)?(?<z>[Zz]|[+-][0-9]{2}(?:[0-9]{2})?)?$", RegexOptions.CultureInvariant)]
	private static partial Regex IsoBasicPattern();

	[GeneratedRegex(@"^(?:(?<dow>Mon|Tue|Wed|Thu|Fri|Sat|Sun)[a-z]*,?\s+)?(?<d>[0-9]{1,2})[\s-]+(?<mon>Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*[\s-]+(?<y>[0-9]{4})\s+(?<h>[0-9]{2}):(?<mi>[0-9]{2})(?::(?<s>[0-9]{2}))?\s*(?<z>GMT|UTC|UT|Z|[+-][0-9]{4})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex RfcPattern();
}
