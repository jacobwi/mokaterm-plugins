using System.Net;
using Moka.Network.Echo;

namespace Moka.Network.Tests.Echo;

public sealed class TracerouteTests
{
	private static readonly IPAddress Destination = IPAddress.Parse("203.0.113.9");

	private static readonly TraceOptions Options = new(30, 3, TimeSpan.FromSeconds(1));

	[Fact]
	public async Task Run_StopsAtTheHopWhereTheDestinationAnswers()
	{
		FakePinger pinger = new((ttl, attempt) => ttl switch
		{
			1 => FakePinger.Router("192.168.1.1", 1),
			2 => attempt == 1 ? EchoReply.TimedOut : FakePinger.Router("10.0.0.1", 8),
			_ => FakePinger.Reply("203.0.113.9", 20),
		});
		List<TraceHop> seen = [];

		TraceEnd end = await Traceroute.RunAsync(pinger, Destination, Options, seen.Add, TestContext.Current.CancellationToken);

		Assert.Equal(TraceEnd.Reached, end);
		Assert.Equal(9, seen.Count);
		Assert.Equal([1, 1, 1, 2, 2, 2, 3, 3, 3], pinger.Requests.Select(request => request.Ttl));
		TraceHop[] hops = [.. seen.GroupBy(hop => hop.Ttl).Select(group => group.Last())];
		Assert.Equal(3, hops.Length);
		Assert.Equal([IPAddress.Parse("192.168.1.1")], hops[0].Addresses);
		Assert.Equal(EchoStatus.TimedOut, hops[1].Probes[1].Status);
		Assert.Null(hops[1].Probes[1].Milliseconds);
		Assert.Equal(8, hops[1].Probes[0].Milliseconds);
		Assert.True(hops[2].Reached);
		Assert.False(hops[1].Reached);
	}

	[Fact]
	public async Task Run_HandsOverEachHopAsItsProbesComeBack()
	{
		FakePinger pinger = new((_, _) => FakePinger.Reply("203.0.113.9"));
		List<int> probes = [];

		await Traceroute.RunAsync(pinger, Destination, Options, hop => probes.Add(hop.Probes.Count), TestContext.Current.CancellationToken);

		Assert.Equal([1, 2, 3], probes);
	}

	[Fact]
	public async Task Run_GoesNoFurtherThanTheLastHop()
	{
		FakePinger pinger = new((_, _) => EchoReply.TimedOut);
		List<TraceHop> seen = [];

		TraceEnd end = await Traceroute.RunAsync(pinger, Destination, Options with { MaxHops = 4 }, seen.Add, TestContext.Current.CancellationToken);

		Assert.Equal(TraceEnd.OutOfHops, end);
		Assert.Equal(12, pinger.Requests.Count);
		Assert.Equal(4, seen.Max(hop => hop.Ttl));
		Assert.True(seen[^1].Silent);
	}

	[Fact]
	public async Task Run_HoldsTheHopCountToThirty()
	{
		FakePinger pinger = new((_, _) => EchoReply.TimedOut);

		await Traceroute.RunAsync(pinger, Destination, Options with { MaxHops = 500, ProbesPerHop = 1 }, _ => { }, TestContext.Current.CancellationToken);

		Assert.Equal(TraceOptions.MaxMaxHops, pinger.Requests.Max(request => request.Ttl));
	}

	[Fact]
	public async Task Run_StopsWhereARouterSaysTheDestinationCannotBeReached()
	{
		FakePinger pinger = new((ttl, _) => ttl == 1
			? FakePinger.Router("192.168.1.1")
			: new EchoReply(EchoStatus.Unreachable, IPAddress.Parse("10.0.0.1"), null, "host unreachable"));
		List<TraceHop> seen = [];

		TraceEnd end = await Traceroute.RunAsync(pinger, Destination, Options, seen.Add, TestContext.Current.CancellationToken);

		Assert.Equal(TraceEnd.Unreachable, end);
		Assert.Equal(2, seen[^1].Ttl);
		Assert.Equal("host unreachable", seen[^1].Unreachable?.Detail);
	}

	[Fact]
	public async Task Run_KeepsAnAnswerWithoutAnAddressAsMissingRatherThanGuessing()
	{
		FakePinger pinger = new((ttl, _) => ttl == 1 ? new EchoReply(EchoStatus.TtlExpired, null, 3) : FakePinger.Reply("203.0.113.9"));
		List<TraceHop> seen = [];

		await Traceroute.RunAsync(pinger, Destination, Options, seen.Add, TestContext.Current.CancellationToken);

		TraceHop first = seen.Last(hop => hop.Ttl == 1);
		Assert.Empty(first.Addresses);
		Assert.True(first.AddressMissing);
		Assert.False(first.Silent);
		Assert.False(seen.Last().AddressMissing);
	}

	[Fact]
	public async Task Run_ListsEveryRouterOfABalancedHopOnce()
	{
		FakePinger pinger = new((ttl, attempt) => ttl == 1
			? FakePinger.Router(attempt == 1 ? "10.0.0.2" : "10.0.0.1")
			: FakePinger.Reply("203.0.113.9"));
		List<TraceHop> seen = [];

		await Traceroute.RunAsync(pinger, Destination, Options, seen.Add, TestContext.Current.CancellationToken);

		Assert.Equal([IPAddress.Parse("10.0.0.1"), IPAddress.Parse("10.0.0.2")], seen.Last(hop => hop.Ttl == 1).Addresses);
	}

	[Fact]
	public async Task Run_StopsWhenCancelled()
	{
		using CancellationTokenSource stop = new();
		FakePinger pinger = new((_, _) => EchoReply.TimedOut);
		pinger.OnSend = () =>
		{
			if (pinger.Requests.Count == 4)
			{
				stop.Cancel();
			}
		};

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Traceroute.RunAsync(pinger, Destination, Options, _ => { }, stop.Token));
		Assert.Equal(4, pinger.Requests.Count);
	}

	[Fact]
	public async Task Run_SendsTheTtlTimeoutAndSizeAsked()
	{
		FakePinger pinger = new((_, _) => FakePinger.Reply("203.0.113.9"));

		await Traceroute.RunAsync(pinger, Destination, new TraceOptions(30, 1, TimeSpan.FromMilliseconds(750), 48), _ => { }, TestContext.Current.CancellationToken);

		Assert.Equal(new EchoRequest(1, TimeSpan.FromMilliseconds(750), 48), Assert.Single(pinger.Requests));
	}

	[Theory]
	[InlineData("10.0.0.1", "203.0.113.9", "10.0.0.1")]
	[InlineData("203.0.113.9", "203.0.113.9", null)]
	[InlineData("0.0.0.0", "203.0.113.9", null)]
	[InlineData("::", "2001:db8::9", null)]
	public void HopAddress_NeverReportsTheDestinationOrNobodyAsTheRouter(string reported, string destination, string? expected) =>
		Assert.Equal(expected is null ? null : IPAddress.Parse(expected), SystemPinger.HopAddress(IPAddress.Parse(reported), IPAddress.Parse(destination)));

	[Fact]
	public void Payload_RepeatsTheAlphabetToTheSizeAsked()
	{
		Assert.Empty(SystemPinger.Payload(0));
		Assert.Equal("abcdefghijklmnopqrstuvwabcdefghiabc"u8.ToArray(), SystemPinger.Payload(35));
	}
}
