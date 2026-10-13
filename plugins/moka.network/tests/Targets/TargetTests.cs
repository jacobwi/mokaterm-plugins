using System.Collections.Concurrent;
using System.Net;
using Moka.Network.Lookup;
using Moka.Network.Targets;

namespace Moka.Network.Tests.Targets;

public sealed class TargetTests
{
	[Theory]
	[InlineData("example.com", "example.com", false)]
	[InlineData("  Example.COM. ", "example.com", false)]
	[InlineData("https://example.com:8443/path?x=1", "example.com", false)]
	[InlineData("192.0.2.1", "192.0.2.1", true)]
	[InlineData("[2001:db8::1]", "2001:db8::1", true)]
	[InlineData("2001:db8::1", "2001:db8::1", true)]
	[InlineData("ssh://[2001:db8::1]:22", "2001:db8::1", true)]
	[InlineData("server-01", "server-01", false)]
	public void Host_ReadsANameAnAddressOrALink(string text, string host, bool isAddress)
	{
		Assert.True(HostInput.TryParse(text, out HostTarget? target, out string? problem), problem);
		Assert.Equal(host, target.Host);
		Assert.Equal(isAddress, target.IsAddress);
	}

	[Theory]
	[InlineData("", "Type a host")]
	[InlineData("10.1", "not a full address")]
	[InlineData("127.1", "not a full address")]
	[InlineData("0x7f.1", "not a full address")]
	[InlineData("167772161", "not a full address")]
	[InlineData("example.com:22", "a port has its own field")]
	[InlineData("*.example.com", "not one machine's name")]
	[InlineData(".", "not one machine's name")]
	[InlineData("bad host", "no spaces")]
	public void Host_RefusesWhatWouldProbeTheWrongMachine(string text, string expected)
	{
		Assert.False(HostInput.TryParse(text, out _, out string? problem));
		Assert.Contains(expected, problem, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("10.0.0.1", true)]
	[InlineData("2001:db8::1", true)]
	[InlineData("fe80::1%12", true)]
	[InlineData("10.1", false)]
	[InlineData("167772161", false)]
	[InlineData("0x0a000001", false)]
	[InlineData("1.2.3.4.5", false)]
	[InlineData("", false)]
	public void Address_TakesOnlyTheFormsPeopleMean(string text, bool expected) =>
		Assert.Equal(expected, AddressText.TryParse(text, out _));

	[Fact]
	public async Task Resolve_UsesAnAddressAsItIs()
	{
		Assert.True(HostInput.TryParse("192.0.2.7", out HostTarget? target, out _));

		IPAddress address = await TargetResolver.ResolveAsync(target, AddressChoice.Any, TargetResolver.DefaultTimeout, TestContext.Current.CancellationToken);

		Assert.Equal(IPAddress.Parse("192.0.2.7"), address);
	}

	[Fact]
	public async Task Resolve_RefusesAnAddressOfTheOtherFamily()
	{
		Assert.True(HostInput.TryParse("192.0.2.7", out HostTarget? target, out _));

		ProbeException error = await Assert.ThrowsAsync<ProbeException>(
			() => TargetResolver.ResolveAsync(target, AddressChoice.IPv6, TargetResolver.DefaultTimeout, TestContext.Current.CancellationToken));

		Assert.Contains("IPv6 is picked", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Resolve_FindsLocalhostThroughTheSystem()
	{
		Assert.True(HostInput.TryParse("localhost", out HostTarget? target, out _));

		IPAddress address = await TargetResolver.ResolveAsync(target, AddressChoice.IPv4, TargetResolver.DefaultTimeout, TestContext.Current.CancellationToken);

		Assert.True(IPAddress.IsLoopback(address));
	}

	[Fact]
	public async Task ReverseNames_AskEachAddressOnceAndAFewAtATime()
	{
		ConcurrentDictionary<IPAddress, int> asked = new();
		ConcurrentDictionary<IPAddress, string> named = new();
		TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
		int active = 0;
		int most = 0;
		using CancellationTokenSource stop = new();
		ReverseNames names = new(
			async (address, token) =>
			{
				asked.AddOrUpdate(address, 1, (_, count) => count + 1);
				int now = Interlocked.Increment(ref active);
				InterlockedMax(ref most, now);
				await release.Task.WaitAsync(token);
				Interlocked.Decrement(ref active);
				return address.ToString().EndsWith(".9", StringComparison.Ordinal) ? null : $"host-{address.GetAddressBytes()[3]}.example";
			},
			(address, name) => named[address] = name,
			stop.Token);

		IPAddress[] addresses = [.. Enumerable.Range(1, 9).Select(index => IPAddress.Parse($"10.0.0.{index}"))];
		names.Request(addresses);
		names.Request(addresses[..3]);
		await WaitUntilAsync(() => Volatile.Read(ref active) == ReverseNames.MaxConcurrent);
		release.SetResult();
		await WaitUntilAsync(() => named.Count == 8);

		Assert.Equal(9, names.Asked);
		Assert.All(asked.Values, count => Assert.Equal(1, count));
		Assert.Equal(ReverseNames.MaxConcurrent, Volatile.Read(ref most));
		Assert.Equal("host-1.example", named[IPAddress.Parse("10.0.0.1")]);
		Assert.False(named.ContainsKey(IPAddress.Parse("10.0.0.9")));
	}

	private static void InterlockedMax(ref int target, int value)
	{
		int current = Volatile.Read(ref target);
		while (value > current && Interlocked.CompareExchange(ref target, value, current) != current)
		{
			current = Volatile.Read(ref target);
		}
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		limit.CancelAfter(TimeSpan.FromSeconds(10));
		while (!condition())
		{
			await Task.Delay(10, limit.Token);
		}
	}
}
