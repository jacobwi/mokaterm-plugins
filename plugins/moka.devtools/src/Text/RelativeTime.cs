using System.Globalization;

namespace Moka.DevTools.Text;

/// <summary>How far a moment is from now in words: "3 hours ago", "in 2 days", "just now".</summary>
public static class RelativeTime
{
	/// <summary>"3 hours ago" for the past, "in 3 hours" for the future.</summary>
	public static string Describe(DateTimeOffset when, DateTimeOffset now)
	{
		TimeSpan gap = when - now;
		if (gap.Duration() < TimeSpan.FromSeconds(1))
		{
			return "just now";
		}

		string span = Span(gap.Duration());
		return gap < TimeSpan.Zero ? $"{span} ago" : $"in {span}";
	}

	/// <summary>A length of time in its largest whole unit: "3 hours", "1 day", "45 seconds".</summary>
	public static string Span(TimeSpan length)
	{
		length = length.Duration();
		double days = length.TotalDays;
		return length switch
		{
			_ when length.TotalSeconds < 60 => Count((long)length.TotalSeconds, "second"),
			_ when length.TotalMinutes < 60 => Count((long)length.TotalMinutes, "minute"),
			_ when length.TotalHours < 48 => Count((long)length.TotalHours, "hour"),
			_ when days < 60 => Count((long)days, "day"),
			_ when days < 730 => Count((long)(days / 30.436875), "month"),
			_ => Count((long)(days / 365.2425), "year"),
		};
	}

	private static string Count(long count, string unit) =>
		count == 1 ? $"1 {unit}" : string.Create(CultureInfo.InvariantCulture, $"{count:N0} {unit}s");
}
