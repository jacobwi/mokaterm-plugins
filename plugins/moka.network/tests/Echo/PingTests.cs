using System.Net;
using Moka.Network.Echo;

namespace Moka.Network.Tests.Echo;

public sealed class PingTests
{
	[Fact]
	public void Statistics_StartEmpty()
	{
		PingStatistics stats = new();

		Assert.Equal(0, stats.Sent);
		Assert.Equal(0, stats.LossPercent);
		Assert.Null(stats.Minimum);
		Assert.Null(stats.Average);
		Assert.Null(stats.Jitter);
	}

	[Fact]
	public void Statistics_CountLossAndWorkOutRoundTripsAndJitter()
	{
		PingStatistics stats = new();

		stats.Add(FakePinger.Reply("192.0.2.1", 10));
		stats.Add(EchoReply.TimedOut);
		stats.Add(FakePinger.Reply("192.0.2.1", 20));
		stats.Add(FakePinger.Reply("192.0.2.1", 15));

		Assert.Equal(4, stats.Sent);
		Assert.Equal(3, stats.Received);
		Assert.Equal(1, stats.Lost);
		Assert.Equal(25, stats.LossPercent);
		Assert.Equal(10, stats.Minimum);
		Assert.Equal(20, stats.Maximum);
		Assert.Equal(15, stats.Average);

		// |20 - 10| and |15 - 20|: the lost request between them does not count as a reply.
		Assert.Equal(7.5, stats.Jitter);
	}

	[Fact]
	public void Statistics_CountAnErrorAnswerAsLost()
	{
		PingStatistics stats = new();

		stats.Add(FakePinger.Router("10.0.0.1"));
		stats.Add(new EchoReply(EchoStatus.Unreachable, IPAddress.Parse("10.0.0.1"), null, "host unreachable"));

		Assert.Equal(2, stats.Lost);
		Assert.Equal(100, stats.LossPercent);
		Assert.Null(stats.Average);
	}

	[Fact]
	public void Statistics_NeedTwoRepliesForJitter()
	{
		PingStatistics stats = new();

		stats.Add(FakePinger.Reply("192.0.2.1", 12));

		Assert.Null(stats.Jitter);
		Assert.Equal(12, stats.Average);
	}

	[Fact]
	public async Task Runner_SendsTheCountAndNumbersEachReply()
	{
		FakePinger pinger = new((_, _) => FakePinger.Reply("192.0.2.1"));
		List<int> sequences = [];
		PingPlan plan = new(IPAddress.Parse("192.0.2.1"), 3, TimeSpan.Zero, new EchoRequest(64, TimeSpan.FromSeconds(1), 56));

		await PingRunner.RunAsync(pinger, plan, (sequence, _) => sequences.Add(sequence), TimeProvider.System, TestContext.Current.CancellationToken);

		Assert.Equal([1, 2, 3], sequences);
		Assert.All(pinger.Requests, request => Assert.Equal(new EchoRequest(64, TimeSpan.FromSeconds(1), 56), request));
	}

	[Fact]
	public async Task Runner_KeepsTheShortestIntervalBetweenRequests()
	{
		FakePinger pinger = new((_, _) => FakePinger.Reply("192.0.2.1"));
		PingPlan plan = new(IPAddress.Parse("192.0.2.1"), 3, TimeSpan.Zero, new EchoRequest(64, TimeSpan.FromSeconds(1), 32));
		long started = TimeProvider.System.GetTimestamp();

		await PingRunner.RunAsync(pinger, plan, (_, _) => { }, TimeProvider.System, TestContext.Current.CancellationToken);

		Assert.True(TimeProvider.System.GetElapsedTime(started) >= PingPlan.MinInterval * 2 - TimeSpan.FromMilliseconds(30));
	}

	[Fact]
	public async Task Runner_RunsUntilStoppedWhenTheCountIsZero()
	{
		using CancellationTokenSource stop = new();
		FakePinger pinger = new((_, _) => FakePinger.Reply("192.0.2.1"));
		List<int> sequences = [];
		PingPlan plan = new(IPAddress.Parse("192.0.2.1"), 0, PingPlan.MinInterval, new EchoRequest(64, TimeSpan.FromSeconds(1), 32));

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PingRunner.RunAsync(pinger, plan, (sequence, _) =>
		{
			sequences.Add(sequence);
			if (sequence == 3)
			{
				stop.Cancel();
			}
		}, TimeProvider.System, stop.Token));

		Assert.True(plan.Continuous);
		Assert.Equal([1, 2, 3], sequences);
	}
}
