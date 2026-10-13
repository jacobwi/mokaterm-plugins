using Moka.DevTools.Dates;

namespace Moka.DevTools.Tests.Dates;

public sealed class TimeTests
{
	private static readonly DateTimeOffset Moment = new(2023, 11, 14, 22, 13, 20, TimeSpan.Zero);

	[Theory]
	[InlineData("1700000000", EpochUnit.Seconds, "10 digits")]
	[InlineData("1700000000000", EpochUnit.Milliseconds, "13 digits")]
	[InlineData("1700000000000000", EpochUnit.Microseconds, "16 digits")]
	[InlineData("1700000000000000000", EpochUnit.Nanoseconds, "19 digits")]
	[InlineData("1_700_000_000", EpochUnit.Seconds, "10 digits")]
	public void Epoch_GuessesTheUnitFromItsSize(string text, EpochUnit unit, string reason)
	{
		TimeReading reading = TimeParser.Read(text);

		Assert.Equal(Moment, reading.Instant);
		Assert.Equal(unit, reading.Unit);
		Assert.Equal(reason, reading.UnitReason);
		Assert.Empty(reading.Notes);
	}

	[Fact]
	public void Epoch_TakesAGivenUnitAndFractions()
	{
		Assert.Equal(DateTimeOffset.UnixEpoch.AddMilliseconds(1700), TimeParser.Read("1700", EpochUnit.Milliseconds).Instant);
		Assert.Null(TimeParser.Read("1700", EpochUnit.Milliseconds).UnitReason);
		Assert.Equal(Moment.AddMilliseconds(500), TimeParser.Read("1700000000.5").Instant);
		Assert.Equal(new DateTimeOffset(1969, 12, 31, 0, 0, 0, TimeSpan.Zero), TimeParser.Read("-86400").Instant);
	}

	[Fact]
	public void Epoch_SaysWhatIsFinerThanATick()
	{
		TimeReading reading = TimeParser.Read("1700000000000000050");

		Assert.Equal(Moment, reading.Instant);
		Assert.Contains(reading.Notes, note => note.Contains("100 ns", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("99999999999999999999999")]
	[InlineData("300000000000", EpochUnit.Seconds)]
	[InlineData("-63000000000", EpochUnit.Seconds)]
	public void Epoch_RefusesWhatNoDateHolds(string text, EpochUnit? unit = null) =>
		Assert.Contains("outside the years", Assert.Throws<FormatException>(() => TimeParser.Read(text, unit)).Message, StringComparison.Ordinal);

	[Theory]
	[InlineData("2023-11-14T22:13:20Z", "RFC 3339")]
	[InlineData("2023-11-14t22:13:20z", "RFC 3339")]
	[InlineData("2023-11-14 23:13:20+01:00", "RFC 3339")]
	[InlineData("2023-11-14T17:13:20-05:00", "RFC 3339")]
	[InlineData("2023-11-14T23:13:20+0100", "ISO 8601")]
	[InlineData("2023-11-14T22:13:20+00", "ISO 8601")]
	[InlineData("20231114T221320Z", "ISO 8601")]
	[InlineData("Tue, 14 Nov 2023 22:13:20 GMT", "RFC 1123")]
	[InlineData("14 Nov 2023 23:13:20 +0100", "RFC 1123")]
	public void Dates_ReadEveryWrittenForm(string text, string source)
	{
		TimeReading reading = TimeParser.Read(text);

		Assert.Equal(Moment, reading.Instant);
		Assert.Equal(source, reading.Source);
		Assert.Null(reading.Unit);
	}

	[Fact]
	public void Dates_KeepFractionsAndSayWhatTheyAssumed()
	{
		TimeReading precise = TimeParser.Read("2023-11-14T22:13:20.1234567891Z");
		TimeReading local = TimeParser.Read("2023-11-14T22:13:20");
		TimeReading day = TimeParser.Read("2023-11-14");

		Assert.Equal(Moment.AddTicks(1_234_567), precise.Instant);
		Assert.Contains(precise.Notes, note => note.Contains("7 digits", StringComparison.Ordinal));
		Assert.Equal(Moment, local.Instant);
		Assert.Null(local.WrittenOffset);
		Assert.Contains(local.Notes, note => note.Contains("read as UTC", StringComparison.Ordinal));
		Assert.Equal(new DateTimeOffset(2023, 11, 14, 0, 0, 0, TimeSpan.Zero), day.Instant);
		Assert.Equal(Moment.AddSeconds(-20), TimeParser.Read("2023-11-14T22:13Z").Instant);
		Assert.Equal("ISO 8601", TimeParser.Read("2023-11-14T22:13Z").Source);
	}

	[Fact]
	public void Dates_NoteADayOfTheWeekThatDoesNotFit()
	{
		TimeReading reading = TimeParser.Read("Mon, 14 Nov 2023 22:13:20 GMT");

		Assert.Equal(Moment, reading.Instant);
		Assert.Contains(reading.Notes, note => note.Contains("Tuesday", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("2023-13-01", "Month 13")]
	[InlineData("2023-02-29", "Day 29")]
	[InlineData("2023-11-14T24:00:00Z", "Hour 24")]
	[InlineData("2023-11-14T22:60:00Z", "Minute 60")]
	[InlineData("2016-12-31T23:59:60Z", "leap second")]
	[InlineData("2023-11-14T22:13:20+15:00", "offsets run")]
	[InlineData("0000-01-01", "year 0")]
	[InlineData("next tuesday", "Not a Unix time")]
	public void Dates_RefuseFieldsOutOfRange(string text, string expected) =>
		Assert.Contains(expected, Assert.Throws<FormatException>(() => TimeParser.Read(text)).Message, StringComparison.Ordinal);

	[Fact]
	public void Formats_WriteEveryForm()
	{
		DateTimeOffset withFraction = Moment.AddTicks(1_500_000);

		Assert.Equal("2023-11-14T22:13:20Z", TimeFormats.IsoUtc(Moment));
		Assert.Equal("2023-11-14T22:13:20.15Z", TimeFormats.IsoUtc(withFraction));
		Assert.Equal("1700000000", TimeFormats.UnixSeconds(Moment));
		Assert.Equal("1700000000.15", TimeFormats.UnixSeconds(withFraction));
		Assert.Equal("1700000000150", TimeFormats.UnixMilliseconds(withFraction));
		Assert.Equal("1700000000150000", TimeFormats.UnixMicroseconds(withFraction));
		Assert.Equal("1700000000150000000", TimeFormats.UnixNanoseconds(withFraction));
		Assert.Equal("Tue, 14 Nov 2023 22:13:20 GMT", TimeFormats.Rfc1123(Moment));
		Assert.Equal("-2.5", TimeFormats.UnixSeconds(DateTimeOffset.UnixEpoch.AddSeconds(-2.5)));
		Assert.Equal("-1", TimeFormats.UnixMilliseconds(DateTimeOffset.UnixEpoch.AddTicks(-1)));
		Assert.Equal("+05:30", TimeFormats.Offset(new TimeSpan(5, 30, 0)));
		Assert.Equal("-08:00", TimeFormats.Offset(TimeSpan.FromHours(-8)));
	}

	[Fact]
	public void Zones_FindIanaNames()
	{
		Assert.True(ZoneCatalog.TryFind("Europe/Berlin", out TimeZoneInfo? berlin, out string? id));
		Assert.Equal("Europe/Berlin", id);
		Assert.Equal("2023-11-14 23:13:20 +01:00", TimeFormats.InZone(Moment, berlin));
		Assert.Equal("2023-07-01 14:00:00 +02:00", TimeFormats.InZone(new DateTimeOffset(2023, 7, 1, 12, 0, 0, TimeSpan.Zero), berlin));
		Assert.False(ZoneCatalog.TryFind("Not/AZone", out _, out _));
		Assert.Contains("UTC", ZoneCatalog.Ids);
		Assert.True(ZoneCatalog.Ids.Count > 20);
	}
}
