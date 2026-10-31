using Moka.Network.Targets;

namespace Moka.Network.Tests.Targets;

/// <summary>The recent-targets memory: order, cap and dedup, with nothing kept beyond the app run it lives in.</summary>
public sealed class RecentTargetsTests
{
	[Fact]
	public void Remember_KeepsTheNewestFirst()
	{
		RecentTargets targets = new();

		targets.Remember("example.com");
		targets.Remember("192.0.2.10");
		targets.Remember("example.org");

		Assert.Equal(["example.org", "192.0.2.10", "example.com"], targets.Recent(RecentTargets.Limit));
	}

	[Fact]
	public void Remember_MovesATargetAskedAgainToTheFrontWithoutDuplicatingIt()
	{
		RecentTargets targets = new();
		targets.Remember("example.com");
		targets.Remember("example.org");
		targets.Remember("example.com");

		Assert.Equal(["example.com", "example.org"], targets.Recent(RecentTargets.Limit));
	}

	[Fact]
	public void Remember_MatchesWithoutCase()
	{
		RecentTargets targets = new();
		targets.Remember("Example.COM");
		targets.Remember("example.com");

		Assert.Equal(["example.com"], targets.Recent(RecentTargets.Limit));
	}

	[Fact]
	public void Remember_KeepsAtMostTheLimitAndDropsTheOldest()
	{
		RecentTargets targets = new();
		foreach (int number in Enumerable.Range(1, RecentTargets.Limit + 2))
		{
			targets.Remember($"host{number}.example");
		}

		IReadOnlyList<string> kept = targets.Recent(RecentTargets.Limit);
		Assert.Equal(RecentTargets.Limit, kept.Count);
		Assert.Equal($"host{RecentTargets.Limit + 2}.example", kept[0]);
		Assert.Equal($"host3.example", kept[^1]);
	}

	[Fact]
	public void Remember_IgnoresWhatIsNoTarget()
	{
		RecentTargets targets = new();

		targets.Remember("");
		targets.Remember("   ");
		targets.Remember(null);

		Assert.Empty(targets.Recent(RecentTargets.Limit));
	}

	[Fact]
	public void Remember_TrimsWhatItKeeps()
	{
		RecentTargets targets = new();

		targets.Remember("  example.com  ");

		Assert.Equal(["example.com"], targets.Recent(RecentTargets.Limit));
	}

	[Fact]
	public void Recent_ReturnsAtMostTheCountAskedFor()
	{
		RecentTargets targets = new();
		targets.Remember("example.com");
		targets.Remember("example.org");
		targets.Remember("example.net");

		Assert.Equal(["example.net", "example.org"], targets.Recent(2));
	}

	[Fact]
	public void Recent_ReturnsACopyThatLaterLookupsDoNotChange()
	{
		RecentTargets targets = new();
		targets.Remember("example.com");

		IReadOnlyList<string> taken = targets.Recent(RecentTargets.Limit);
		targets.Remember("example.org");

		Assert.Equal(["example.com"], taken);
	}
}
