using Moka.DevTools.Components;
using Moka.DevTools.Dates;
using Moka.DevTools.Tests.Shared;

namespace Moka.DevTools.Tests.Dates;

public sealed class TimeViewTests
{
	[Fact]
	public async Task Reading_ShowsTilesAndEveryForm()
	{
		TimeReading reading = TimeParser.Read("1700000000.15");

		string html = await Render.HtmlAsync<TimeReadingView>(new() { [nameof(TimeReadingView.Reading)] = reading });

		Assert.Contains("23 months ago", html, StringComparison.Ordinal);
		Assert.Contains("22:13:20.15", html, StringComparison.Ordinal);
		Assert.Contains("Tue 2023-11-14", html, StringComparison.Ordinal);
		Assert.Contains(">Tuesday<", html, StringComparison.Ordinal);
		Assert.Contains("ISO week 46", html, StringComparison.Ordinal);
		Assert.Contains("2023-11-14T22:13:20.15Z", html, StringComparison.Ordinal);
		Assert.Contains("Tue, 14 Nov 2023 22:13:20 GMT", html, StringComparison.Ordinal);
		Assert.Contains("1700000000150000000", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Copy the Unix milliseconds\"", html, StringComparison.Ordinal);
		Assert.Contains("Read as Unix seconds", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Zones_ShowTheMomentInEach()
	{
		Assert.True(ZoneCatalog.TryFind("Asia/Kolkata", out TimeZoneInfo? kolkata, out string? kolkataId));
		Assert.True(ZoneCatalog.TryFind("Europe/Berlin", out TimeZoneInfo? berlin, out string? berlinId));
		DateTimeOffset moment = new(2023, 11, 14, 22, 13, 20, TimeSpan.Zero);

		string html = await Render.HtmlAsync<ZoneTable>(new()
		{
			[nameof(ZoneTable.Zones)] = new List<ZoneEntry> { new(kolkataId, kolkata), new(berlinId, berlin) },
			[nameof(ZoneTable.Instant)] = moment,
		});

		Assert.Contains("Asia/Kolkata", html, StringComparison.Ordinal);
		Assert.Contains("2023-11-15 03:43:20 +05:30", html, StringComparison.Ordinal);
		Assert.Contains("2023-11-14 23:13:20 +01:00", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Remove Europe/Berlin\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Zones_SayWhatToDoWhenThereAreNone()
	{
		string html = await Render.HtmlAsync<ZoneTable>(new() { [nameof(ZoneTable.Zones)] = Array.Empty<ZoneEntry>() });

		Assert.Contains("No zones yet", html, StringComparison.Ordinal);
	}

	[Fact]
	public void Clock_And_Day_ReadInTheZoneGiven()
	{
		DateTimeOffset moment = new(2023, 11, 14, 22, 13, 20, TimeSpan.Zero);
		Assert.True(ZoneCatalog.TryFind("Asia/Kolkata", out TimeZoneInfo? kolkata, out _));

		Assert.Equal("22:13:20", TimeFormats.Clock(moment, TimeZoneInfo.Utc));
		Assert.Equal("22:13:20.25", TimeFormats.Clock(moment.AddMilliseconds(250), TimeZoneInfo.Utc));
		Assert.Equal("03:43:20", TimeFormats.Clock(moment, kolkata));
		Assert.Equal("Tue 2023-11-14", TimeFormats.Day(moment, TimeZoneInfo.Utc));
		Assert.Equal("Wed 2023-11-15", TimeFormats.Day(moment, kolkata));
	}
}
