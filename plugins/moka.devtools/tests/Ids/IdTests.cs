using Moka.DevTools.Ids;

namespace Moka.DevTools.Tests.Ids;

public sealed class IdTests
{
	private static readonly DateTimeOffset RfcExampleTime = new(2022, 2, 22, 19, 22, 22, TimeSpan.Zero);

	[Fact]
	public void Ulid_ReadsTheSpecExample()
	{
		Ulid ulid = Ulid.Parse("01ARZ3NDEKTSV4RRFFQ69G5FAV", out bool aliases);

		Assert.False(aliases);
		Assert.Equal(1_469_922_850_259, ulid.Milliseconds);
		Assert.Equal(new DateTimeOffset(2016, 7, 30, 23, 54, 10, 259, TimeSpan.Zero), ulid.Time);
		Assert.Equal("01ARZ3NDEKTSV4RRFFQ69G5FAV", ulid.ToString());
		Assert.Equal(ulid, Ulid.Parse("01arz3ndektsv4rrffq69g5fav", out _));
	}

	[Fact]
	public void Ulid_ReadsCrockfordAliases()
	{
		Ulid ulid = Ulid.Parse("0IARZ3NDEKTSV4RRFFQ69G5FAV", out bool aliases);

		Assert.True(aliases);
		Assert.Equal(Ulid.Parse("01ARZ3NDEKTSV4RRFFQ69G5FAV", out _), ulid);
		Assert.Equal(Ulid.Parse("0LARZ3NDEKTSV4RRFFQ69G5FAV", out _), ulid);
		Assert.Equal(Ulid.Parse("O1ARZ3NDEKTSV4RRFFQ69G5FAV", out _), ulid);
	}

	[Theory]
	[InlineData("01ARZ3NDEKTSV4RRFFQ69G5FA", "26 characters")]
	[InlineData("01ARZ3NDEKTSV4RRFFQ69G5FAU", "character 26")]
	[InlineData("81ARZ3NDEKTSV4RRFFQ69G5FAV", "128 bits")]
	public void Ulid_RefusesWhatIsNotOne(string text, string expected) =>
		Assert.Contains(expected, Assert.Throws<FormatException>(() => Ulid.Parse(text, out _)).Message, StringComparison.Ordinal);

	[Fact]
	public void Ulid_PutsTheTimeFirstAndRandomAfter()
	{
		byte[] random = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

		Ulid ulid = Ulid.Create(0x0102_0304_0506, random);

		Assert.Equal([1, 2, 3, 4, 5, 6, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10], ulid.ToBytes());
		Assert.Equal(0x0102_0304_0506, ulid.Milliseconds);
		Assert.Equal(Ulid.FromBytes(ulid.ToBytes()), ulid);
		Assert.Equal(26, ulid.ToString().Length);
	}

	[Fact]
	public void Ulid_BatchesCountUpWithinAMillisecond()
	{
		DateTimeOffset now = new(2026, 10, 12, 8, 0, 0, TimeSpan.Zero);

		IReadOnlyList<Ulid> batch = Ulid.CreateMany(1000, now);

		Assert.All(batch, ulid => Assert.Equal(now.ToUnixTimeMilliseconds(), ulid.Milliseconds));
		for (int index = 1; index < batch.Count; index++)
		{
			Assert.Equal(batch[index - 1].Value + 1, batch[index].Value);
			Assert.True(string.CompareOrdinal(batch[index - 1].ToString(), batch[index].ToString()) < 0);
		}
	}

	[Fact]
	public void Inspect_ReadsTheRfcVersion1Example()
	{
		IdReport report = IdInspector.Inspect("C232AB00-9414-11EC-B3C8-9F6BDECED846");

		Assert.Equal("UUID", report.Kind);
		Assert.Equal("c232ab00-9414-11ec-b3c8-9f6bdeced846", report.Canonical);
		Assert.Equal(1, report.Version);
		Assert.Equal("RFC 9562", report.Variant);
		Assert.Equal(RfcExampleTime, report.Time);
		Assert.Contains(report.Facts, fact => fact.Label == "Clock sequence" && fact.Value == "13256");
		Assert.Contains(report.Facts, fact => fact.Label == "Node" && fact.Value.StartsWith("9f:6b:de:ce:d8:46", StringComparison.Ordinal));
	}

	[Fact]
	public void Inspect_ReadsTheRfcVersion6And7Examples()
	{
		IdReport v6 = IdInspector.Inspect("1EC9414C-232A-6B00-B3C8-9F6BDECED846");
		IdReport v7 = IdInspector.Inspect("017F22E2-79B0-7CC3-98C4-DC0C0C07398F");
		IdReport v4 = IdInspector.Inspect("919108f7-52d1-4320-9bac-f847db4148a8");

		Assert.Equal(6, v6.Version);
		Assert.Equal(RfcExampleTime, v6.Time);
		Assert.Equal(7, v7.Version);
		Assert.Equal(RfcExampleTime, v7.Time);
		Assert.Equal(4, v4.Version);
		Assert.Null(v4.Time);
		Assert.StartsWith("v4, random", v4.Summary, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("{919108F7-52D1-4320-9BAC-F847DB4148A8}")]
	[InlineData("urn:uuid:919108f7-52d1-4320-9bac-f847db4148a8")]
	[InlineData("919108f752d143209bacf847db4148a8")]
	[InlineData("  (919108f7-52d1-4320-9bac-f847db4148a8) ")]
	public void Inspect_TakesTheUsualWrappings(string text) =>
		Assert.Equal("919108f7-52d1-4320-9bac-f847db4148a8", IdInspector.Inspect(text).Canonical);

	[Fact]
	public void Inspect_KnowsNilAndMax()
	{
		Assert.StartsWith("Nil", IdInspector.Inspect("00000000-0000-0000-0000-000000000000").Summary, StringComparison.Ordinal);
		Assert.StartsWith("Max", IdInspector.Inspect("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF").Summary, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("919108f7-52d1-4320-9bac-f847db4148ag", "character 36")]
	[InlineData("919108f7_52d1-4320-9bac-f847db4148a8", "character 9")]
	[InlineData("919108f7", "32 hex digits")]
	public void Inspect_SaysWhereAUuidBreaks(string text, string expected) =>
		Assert.Contains(expected, Assert.Throws<FormatException>(() => IdInspector.Inspect(text)).Message, StringComparison.Ordinal);

	[Fact]
	public void Inspect_ConvertsBetweenUlidAndUuid()
	{
		IdReport ulid = IdInspector.Inspect("01ARZ3NDEKTSV4RRFFQ69G5FAV");
		IdReport uuid = IdInspector.Inspect("01563e3a-b5d3-d676-4c61-efb99302bd5b");

		Assert.Equal("ULID", ulid.Kind);
		Assert.Contains(ulid.Facts, fact => fact.Label == "As UUID" && fact.Value == "01563e3a-b5d3-d676-4c61-efb99302bd5b");
		Assert.Contains(uuid.Facts, fact => fact.Label == "As ULID" && fact.Value == "01ARZ3NDEKTSV4RRFFQ69G5FAV");
	}

	[Fact]
	public void Generate_MakesEachKindInTheFormAskedFor()
	{
		DateTimeOffset now = DateTimeOffset.UtcNow;

		IReadOnlyList<string> v4 = IdGenerator.Generate(IdKind.UuidV4, 5, upper: true, hyphens: false, now);
		IReadOnlyList<string> v7 = IdGenerator.Generate(IdKind.UuidV7, 3, upper: false, hyphens: true, now);
		IReadOnlyList<string> ulids = IdGenerator.Generate(IdKind.Ulid, 2, upper: false, hyphens: true, now);

		Assert.All(v4, id =>
		{
			Assert.Equal(32, id.Length);
			Assert.Equal(id.ToUpperInvariant(), id);
			Assert.Equal(4, IdInspector.Inspect(id).Version);
		});
		Assert.All(v7, id =>
		{
			IdReport report = IdInspector.Inspect(id);
			Assert.Equal(7, report.Version);
			Assert.Equal(now.ToUnixTimeMilliseconds(), report.Time!.Value.ToUnixTimeMilliseconds());
		});
		Assert.All(ulids, id => Assert.Equal(id.ToLowerInvariant(), id));
		Assert.Equal(IdGenerator.MaxCount, IdGenerator.Generate(IdKind.UuidV4, 5000, upper: false, hyphens: true, now).Count);
	}
}
