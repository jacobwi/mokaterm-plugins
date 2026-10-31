using System.Text.RegularExpressions;
using Moka.DevTools.Patterns;

namespace Moka.DevTools.Tests.Patterns;

/// <summary>
/// Every preset compiles as a .NET regex and tells a sample it should match from one it should not. The Regex section
/// runs them under JavaScript's engine, whose syntax these patterns keep to.
/// </summary>
public sealed class RegexPresetTests
{
	private static readonly Dictionary<string, (string Sample, string CounterSample)> Samples = new()
	{
		["IPv4 address"] = ("The host 192.168.1.1 is down", "It only got as far as 999.168.1"),
		["IPv6 address"] = ("2001:0db8:85a3:0000:0000:8a2e:0370:7334", "2001:db8::8a2e:370:7334"),
		["Email address"] = ("Write to user.name+tag@example.co.uk", "That address, user@example, has no domain"),
		["HTTP(S) URL"] = ("Read https://example.com/docs?q=1 today", "Fetch ftp://example.com/file.txt"),
		["ISO 8601 date"] = ("At 2026-10-31T12:34:56Z and on 2026-11-01", "31-10-2026 12:34"),
		["UUID"] = ("id 550e8400-e29b-41d4-a716-446655440000", "id 550e8400-e29b-41d4-a716-44665544000"),
		["Hex colour"] = ("The theme uses #1a2b3c and #fff", "Not #12 and not #12345"),
		["MAC address"] = ("Adapter 00:1A:2B:3C:4D:5E", "Adapter 00:1A:2B:3C"),
		["JWT"] = ("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.SflKxwRJSMeKKF2Q", "Authorization: Bearer eyJhbGciOiJIUzI1NiJ9"),
		["Semantic version"] = ("Ships as 1.2.3-beta.1+build.5", "Still at 1.2"),
		["Decimal number"] = ("It costs 19.99 today", "No digits in this one"),
		["Cron schedule"] = ("Run at */5 9-17 1 * 1", "Run at * * *"),
	};

	public static TheoryData<RegexPreset> Presets()
	{
		TheoryData<RegexPreset> data = [];
		foreach (RegexPreset preset in RegexPresets.All)
		{
			data.Add(preset);
		}

		return data;
	}

	[Theory]
	[MemberData(nameof(Presets))]
	public void EveryPreset_CompilesAndMatchesItsSample(RegexPreset preset)
	{
		Regex regex = new(preset.Pattern, RegexOptions.CultureInvariant);

		Assert.True(Samples.TryGetValue(preset.Name, out (string Sample, string CounterSample) samples),
			$"{preset.Name} has no samples in this test.");
		Assert.Matches(regex, samples.Sample);
		Assert.DoesNotMatch(regex, samples.CounterSample);
	}

	[Fact]
	public void ThePresets_HaveDistinctNamesAndSayWhatTheyAre()
	{
		Assert.Equal(RegexPresets.All.Count, RegexPresets.All.Select(preset => preset.Name).Distinct(StringComparer.Ordinal).Count());
		Assert.All(RegexPresets.All, preset =>
		{
			Assert.False(string.IsNullOrWhiteSpace(preset.Name));
			Assert.False(string.IsNullOrWhiteSpace(preset.Description));
			Assert.False(string.IsNullOrWhiteSpace(preset.Pattern));
		});
	}

	[Fact]
	public void PatternsThatNameGroups_HaveTheGroupsTheirDescriptionsPromise()
	{
		Dictionary<string, string[]> expected = new()
		{
			["ISO 8601 date"] = ["year", "month", "day"],
			["Semantic version"] = ["major", "minor", "patch"],
			["Decimal number"] = ["number"],
			["Cron schedule"] = ["minute", "hour", "day", "month", "weekday"],
		};

		foreach ((string name, string[] groups) in expected)
		{
			Regex regex = new(RegexPresets.All.Single(preset => preset.Name == name).Pattern);
			Assert.All(groups, group => Assert.Contains(group, regex.GetGroupNames()));
		}
	}
}
