using System.Globalization;
using System.Numerics;

namespace Moka.DevTools.Dates;

/// <summary>One moment written every way the Time section shows it.</summary>
public static class TimeFormats
{
	/// <summary>ISO 8601 in UTC with a Z, the fraction only as long as it needs to be.</summary>
	public static string IsoUtc(DateTimeOffset instant)
	{
		string text = instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
		return text + "Z";
	}

	/// <summary>The moment in <paramref name="zone"/>, with its offset: "2024-05-01 14:00:00 +02:00".</summary>
	public static string InZone(DateTimeOffset instant, TimeZoneInfo zone)
	{
		ArgumentNullException.ThrowIfNull(zone);
		DateTimeOffset local = TimeZoneInfo.ConvertTime(instant, zone);
		string time = local.ToString("yyyy-MM-dd HH:mm:ss.fffffff", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
		return $"{time} {Offset(local.Offset)}";
	}

	/// <summary>"+02:00", "-05:30", "+00:00".</summary>
	public static string Offset(TimeSpan offset) =>
		(offset < TimeSpan.Zero ? "-" : "+") + offset.Duration().ToString(@"hh\:mm", CultureInfo.InvariantCulture);

	/// <summary>The zone's name for that moment: its daylight name in summer, its standard name otherwise.</summary>
	public static string ZoneName(DateTimeOffset instant, TimeZoneInfo zone)
	{
		ArgumentNullException.ThrowIfNull(zone);
		return zone.IsDaylightSavingTime(instant) ? zone.DaylightName : zone.StandardName;
	}

	public static string Rfc1123(DateTimeOffset instant) => instant.UtcDateTime.ToString("r", CultureInfo.InvariantCulture);

	/// <summary>Whole seconds, or seconds and the fraction when the moment has one; exact, so -2.5 is two and a half before 1970.</summary>
	public static string UnixSeconds(DateTimeOffset instant)
	{
		BigInteger ticks = Ticks(instant);
		BigInteger whole = BigInteger.DivRem(BigInteger.Abs(ticks), TimeSpan.TicksPerSecond, out BigInteger part);
		string text = whole.ToString(CultureInfo.InvariantCulture);
		if (!part.IsZero)
		{
			text += "." + part.ToString("D7", CultureInfo.InvariantCulture).TrimEnd('0');
		}

		return ticks < 0 ? "-" + text : text;
	}

	public static string UnixMilliseconds(DateTimeOffset instant) => Scaled(instant, TimeSpan.TicksPerMillisecond);

	public static string UnixMicroseconds(DateTimeOffset instant) => Scaled(instant, 10);

	public static string UnixNanoseconds(DateTimeOffset instant) => (Ticks(instant) * 100).ToString(CultureInfo.InvariantCulture);

	private static BigInteger Ticks(DateTimeOffset instant) => new BigInteger(instant.UtcTicks) - DateTimeOffset.UnixEpoch.UtcTicks;

	// Truncated toward the earlier moment, the way a clock reads: 1.9 ms is 1 ms.
	private static string Scaled(DateTimeOffset instant, long ticksPerUnit)
	{
		BigInteger ticks = Ticks(instant);
		BigInteger value = BigInteger.DivRem(ticks, ticksPerUnit, out BigInteger rest);
		if (rest < 0)
		{
			value -= 1;
		}

		return value.ToString(CultureInfo.InvariantCulture);
	}
}
