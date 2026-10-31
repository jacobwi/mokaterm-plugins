using System.Net;
using Moka.Network.Lookup.Mapping;
using Moka.Network.Subnets;

namespace Moka.Network.Tests.Mapping;

public sealed class PtrSweepTests
{
	[Fact]
	public void TryAddresses_ListsEveryAddressOfASmallPrefixInOrder()
	{
		SubnetInfo subnet = SubnetMath.Calculate(IPAddress.Parse("192.0.2.10"), 29);

		Assert.True(PtrSweep.TryAddresses(subnet, out IReadOnlyList<IPAddress>? addresses, out string? problem));

		Assert.Null(problem);
		Assert.Equal(
			["192.0.2.8", "192.0.2.9", "192.0.2.10", "192.0.2.11", "192.0.2.12", "192.0.2.13", "192.0.2.14", "192.0.2.15"],
			addresses.Select(address => address.ToString()));
	}

	[Fact]
	public void TryAddresses_TakesAWholeSlash24IncludingItsEnds()
	{
		SubnetInfo subnet = SubnetMath.Calculate(IPAddress.Parse("192.168.1.0"), 24);

		Assert.True(PtrSweep.TryAddresses(subnet, out IReadOnlyList<IPAddress>? addresses, out _));

		Assert.Equal(PtrSweep.MaxAddresses, addresses.Count);
		Assert.Equal("192.168.1.0", addresses[0].ToString());
		Assert.Equal("192.168.1.255", addresses[^1].ToString());
	}

	[Fact]
	public void TryAddresses_RefusesAPrefixWiderThanASlash24InPlainWords()
	{
		SubnetInfo subnet = SubnetMath.Calculate(IPAddress.Parse("192.168.0.0"), 23);

		Assert.False(PtrSweep.TryAddresses(subnet, out IReadOnlyList<IPAddress>? addresses, out string? problem));

		Assert.Null(addresses);
		Assert.Contains("512 addresses", problem, StringComparison.Ordinal);
		Assert.Contains("/24", problem, StringComparison.Ordinal);
	}

	[Fact]
	public void TryAddresses_RefusesAnIpv6PrefixInPlainWords()
	{
		SubnetInfo subnet = SubnetMath.Calculate(IPAddress.Parse("2001:db8::"), 64);

		Assert.False(PtrSweep.TryAddresses(subnet, out IReadOnlyList<IPAddress>? addresses, out string? problem));

		Assert.Null(addresses);
		Assert.Contains("IPv6", problem, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_ListsNamesAgainstSilenceInAddressOrder()
	{
		IReadOnlyList<IPAddress> addresses =
		[
			IPAddress.Parse("192.168.1.1"),
			IPAddress.Parse("192.168.1.2"),
			IPAddress.Parse("192.168.1.3"),
		];

		PtrSweepResult sweep = await PtrSweep.RunAsync(
			(address, _) => Task.FromResult<string?>(address.GetAddressBytes()[3] % 2 == 1 ? $"host{address.GetAddressBytes()[3]}.example.lan" : null),
			addresses,
			_ => { },
			CancellationToken.None);

		Assert.Equal(3, sweep.Items.Count);
		Assert.Equal("host1.example.lan", sweep.Items[0].Name);
		Assert.True(sweep.Items[1].Silent);
		Assert.Equal("host3.example.lan", sweep.Items[2].Name);
		Assert.Equal(2, sweep.Found);
		Assert.Equal(1, sweep.Silent);
	}

	[Fact]
	public async Task RunAsync_KeepsAtMostEightQuestionsInFlight()
	{
		IReadOnlyList<IPAddress> addresses = [.. Enumerable.Range(0, 40).Select(index => IPAddress.Parse($"192.0.2.{index}"))];
		int inFlight = 0;
		int highest = 0;

		PtrSweepResult sweep = await PtrSweep.RunAsync(
			async (address, cancellationToken) =>
			{
				int now = Interlocked.Increment(ref inFlight);
				Interlocked.Exchange(ref highest, Math.Max(highest, now));
				await Task.Delay(20, cancellationToken);
				Interlocked.Decrement(ref inFlight);
				return null;
			},
			addresses,
			_ => { },
			CancellationToken.None);

		Assert.Equal(addresses.Count, sweep.Items.Count);
		Assert.InRange(highest, 2, PtrSweep.MaxAtOnce);
	}

	[Fact]
	public async Task RunAsync_ReportsProgressUntilEveryAddressIsAnswered()
	{
		IReadOnlyList<IPAddress> addresses = [.. Enumerable.Range(0, 12).Select(index => IPAddress.Parse($"192.0.2.{index}"))];
		List<int> reports = [];

		await PtrSweep.RunAsync((_, _) => Task.FromResult<string?>("host.example.lan"), addresses, done => reports.Add(done), CancellationToken.None);

		Assert.Equal(addresses.Count, reports.Count);
		Assert.Equal(addresses.Count, reports.Max());
		Assert.Equal(Enumerable.Range(1, addresses.Count).ToHashSet(), reports.ToHashSet());
	}

	[Fact]
	public async Task RunAsync_StopsWhenCancelled()
	{
		IReadOnlyList<IPAddress> addresses = [.. Enumerable.Range(0, 16).Select(index => IPAddress.Parse($"192.0.2.{index}"))];
		using CancellationTokenSource cancelled = new();
		await cancelled.CancelAsync();

		// TaskCanceledException, which the gate's wait throws, is an OperationCanceledException.
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			PtrSweep.RunAsync((_, token) => Task.FromResult<string?>(null), addresses, _ => { }, cancelled.Token));
	}
}
