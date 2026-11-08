using Moka.Kubernetes.Server;

namespace Moka.Kubernetes.Tests.Server;

/// <summary>The word columns kubectl prints, read as the numbers they stand for so a table sorts them by meaning.</summary>
public sealed class KubectlColumnsTests
{
	[Theory]
	[InlineData("45s", 45L)]
	[InlineData("8m", 480L)]
	[InlineData("23h", 82_800L)]
	[InlineData("5d2h", 439_200L)]
	[InlineData("2y40d", 66_528_000L)]
	public void AgeSeconds_ReadsKubectlsShortForm(string age, long seconds) => Assert.Equal(seconds, KubectlColumns.AgeSeconds(age));

	[Theory]
	[InlineData("")]
	[InlineData("<unknown>")]
	[InlineData("5")]
	[InlineData("d5")]
	[InlineData("5x")]
	public void AgeSeconds_LeavesAnythingElseUnread(string age) => Assert.Null(KubectlColumns.AgeSeconds(age));

	[Fact]
	public void ReadyShare_ReadsACountOfReadyPods()
	{
		Assert.Equal(0.25, KubectlColumns.ReadyShare("1/4"));
		Assert.Equal((2, 3), KubectlColumns.ReadyCounts("2/3"));
		Assert.Null(KubectlColumns.ReadyShare("0/0"));
		Assert.Null(KubectlColumns.ReadyShare("ready"));
	}

	[Fact]
	public void Restarts_ReadsTheCountBeforeTheRecentRestartNote()
	{
		Assert.Equal(5, KubectlColumns.Restarts("5 (2m ago)"));
		Assert.Equal(12, KubectlColumns.Restarts("12"));
		Assert.Null(KubectlColumns.Restarts("-"));
	}

	[Fact]
	public void Compare_PutsWhatDidNotReadAfterWhatDid()
	{
		Assert.True(KubectlColumns.Compare<long>(5, 7) < 0);
		Assert.True(KubectlColumns.Compare<long>(null, 7) > 0);
		Assert.True(KubectlColumns.Compare<long>(7, null) < 0);
		Assert.Equal(0, KubectlColumns.Compare<long>(null, null));
	}
}
