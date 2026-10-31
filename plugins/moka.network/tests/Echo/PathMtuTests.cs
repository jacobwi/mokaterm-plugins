using Moka.Network.Echo;

namespace Moka.Network.Tests.Echo;

/// <summary>The path MTU search against a fake that decides which sizes pass, so every boundary case is exact.</summary>
public sealed class PathMtuTests
{
	[Fact]
	public async Task Find_ReportsTheClassicEthernetCeilingWhenEverySizePasses()
	{
		PathMtuResult result = await PathMtu.FindAsync((_, _) => Task.FromResult(true), TestContext.Current.CancellationToken);

		Assert.Equal(PathMtu.MaxPayload, result.LargestPayload);
		Assert.Equal(1500, result.PathMtuBytes);
		Assert.True(result.AtCeiling);
	}

	[Fact]
	public async Task Find_ReportsNothingWhenEvenAnEmptyPacketFails()
	{
		PathMtuResult result = await PathMtu.FindAsync((_, _) => Task.FromResult(false), TestContext.Current.CancellationToken);

		Assert.Null(result.LargestPayload);
		Assert.Null(result.PathMtuBytes);
		Assert.False(result.AtCeiling);
	}

	[Fact]
	public async Task Find_FindsTheClassicPppoeCeiling()
	{
		PathMtuResult result = await PathMtu.FindAsync((size, _) => Task.FromResult(size <= 1464), TestContext.Current.CancellationToken);

		Assert.Equal(1464, result.LargestPayload);
		Assert.Equal(1492, result.PathMtuBytes);
		Assert.False(result.AtCeiling);
	}

	[Theory]
	[InlineData(0, 28)]
	[InlineData(1, 29)]
	[InlineData(100, 128)]
	[InlineData(1471, 1499)]
	public async Task Find_StopsAtTheBoundary(int ceiling, int expectedMtu)
	{
		PathMtuResult result = await PathMtu.FindAsync((size, _) => Task.FromResult(size <= ceiling), TestContext.Current.CancellationToken);

		Assert.Equal(ceiling, result.LargestPayload);
		Assert.Equal(expectedMtu, result.PathMtuBytes);
	}

	[Fact]
	public async Task Find_AsksEachSizeWithinTheRangeAndNoMoreThanNeeded()
	{
		List<int> asked = [];

		await PathMtu.FindAsync(
			(size, _) =>
			{
				asked.Add(size);
				return Task.FromResult(size <= 1400);
			},
			TestContext.Current.CancellationToken);

		Assert.True(asked.Count <= 11, $"asked {asked.Count} times");
		Assert.Equal(asked.Count, asked.Distinct().Count());
		Assert.All(asked, size => Assert.InRange(size, PathMtu.MinPayload, PathMtu.MaxPayload));
	}

	[Fact]
	public async Task Find_StopsWhenCancelled()
	{
		using CancellationTokenSource stop = new();
		await stop.CancelAsync();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => PathMtu.FindAsync((_, token) => Task.FromException<bool>(new OperationCanceledException(token)), stop.Token));
	}
}
