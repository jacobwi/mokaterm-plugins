using System.Net;
using Moka.Network.Lookup;

namespace Moka.Network.Tests.Lookup;

/// <summary>The resolver race against a fake lookup that answers with chosen delays, failures and codes.</summary>
public sealed class ResolverRaceTests
{
	private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

	[Fact]
	public async Task Run_RanksAnswersFastestFirstAndFailuresLast()
	{
		ResolverRaceEntry fast = new("Fast", [Endpoint(1)]);
		ResolverRaceEntry slow = new("Slow", [Endpoint(2)]);
		ResolverRaceEntry dead = new("Dead", [Endpoint(3)]);

		IReadOnlyList<ResolverRaceResult> results = await ResolverRace.RunAsync(
			[dead, slow, fast], "example.com", 1, Limit, Lookup, TestContext.Current.CancellationToken);

		Assert.Equal(["Fast", "Slow", "Dead"], results.Select(result => result.Entry.Label));
		Assert.All(results.Take(2), result => Assert.True(result.Answered));
		Assert.False(results[2].Answered);

		async Task<DnsLookupResult> Lookup(IReadOnlyList<IPEndPoint> servers, string name, int type, TimeSpan timeout, CancellationToken cancellationToken)
		{
			if (servers == fast.Servers)
			{
				await Task.Delay(10, cancellationToken);
				return RaceReplies.Address("192.0.2.10");
			}

			if (servers == slow.Servers)
			{
				await Task.Delay(80, cancellationToken);
				return RaceReplies.Address("192.0.2.20");
			}

			await Task.Delay(40, cancellationToken);
			throw new DnsLookupException("192.0.2.3:53 did not answer within 5 s.");
		}
	}

	[Fact]
	public async Task Run_RanksAnErrorCodeAnswerAheadOfAFailure()
	{
		ResolverRaceEntry refused = new("Refused", [Endpoint(1)]);
		ResolverRaceEntry dead = new("Dead", [Endpoint(2)]);

		IReadOnlyList<ResolverRaceResult> results = await ResolverRace.RunAsync(
			[dead, refused], "example.com", 1, Limit, Lookup, TestContext.Current.CancellationToken);

		Assert.Equal("Refused", results[0].Entry.Label);
		Assert.True(results[0].Answered);
		Assert.Equal("Dead", results[1].Entry.Label);

		async Task<DnsLookupResult> Lookup(IReadOnlyList<IPEndPoint> servers, string name, int type, TimeSpan timeout, CancellationToken cancellationToken)
		{
			if (servers == refused.Servers)
			{
				await Task.Delay(60, cancellationToken);
				return RaceReplies.Code(5);
			}

			await Task.Delay(5, cancellationToken);
			throw new DnsLookupException("192.0.2.2:53 did not answer within 5 s.");
		}
	}

	[Fact]
	public async Task Run_CarriesTheFailureAndTheWholeTryTook()
	{
		ResolverRaceEntry dead = new("Dead", [Endpoint(1)]);
		long started = TimeProvider.System.GetTimestamp();

		IReadOnlyList<ResolverRaceResult> results = await ResolverRace.RunAsync(
			[dead], "example.com", 1, Limit, Lookup, TestContext.Current.CancellationToken);

		ResolverRaceResult failure = Assert.Single(results);
		Assert.Null(failure.Result);
		Assert.Contains("did not answer", failure.Failure, StringComparison.Ordinal);
		Assert.True(failure.Elapsed >= TimeSpan.FromMilliseconds(30));
		Assert.True(failure.Elapsed < TimeSpan.FromSeconds(5));

		async Task<DnsLookupResult> Lookup(IReadOnlyList<IPEndPoint> servers, string name, int type, TimeSpan timeout, CancellationToken cancellationToken)
		{
			await Task.Delay(30, cancellationToken);
			throw new DnsLookupException("192.0.2.1:53 did not answer within 5 s.");
		}
	}

	[Fact]
	public async Task Run_StopsWhenCancelled()
	{
		using CancellationTokenSource stop = new();
		await stop.CancelAsync();
		ResolverRaceEntry entry = new("Fast", [Endpoint(1)]);

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => ResolverRace.RunAsync([entry], "example.com", 1, Limit, Lookup, stop.Token));

		async Task<DnsLookupResult> Lookup(IReadOnlyList<IPEndPoint> servers, string name, int type, TimeSpan timeout, CancellationToken cancellationToken)
		{
			await Task.Delay(10, cancellationToken);
			return RaceReplies.Address("192.0.2.10");
		}
	}

	[Fact]
	public async Task Run_PassesTheQuestionToEveryResolver()
	{
		ResolverRaceEntry one = new("One", [Endpoint(1)]);
		ResolverRaceEntry two = new("Two", [Endpoint(2)]);
		List<(IReadOnlyList<IPEndPoint> Servers, string Name, int Type, TimeSpan Timeout)> asked = [];

		await ResolverRace.RunAsync([one, two], "example.com", 16, Limit, Lookup, TestContext.Current.CancellationToken);

		Assert.Equal(2, asked.Count);
		Assert.All(asked, question => Assert.Equal(("example.com", 16, Limit), (question.Name, question.Type, question.Timeout)));
		Assert.Equal([one.Servers, two.Servers], asked.Select(question => question.Servers));

		Task<DnsLookupResult> Lookup(IReadOnlyList<IPEndPoint> servers, string name, int type, TimeSpan timeout, CancellationToken cancellationToken)
		{
			asked.Add((servers, name, type, timeout));
			return Task.FromResult(RaceReplies.Address("\"hello\"", 16));
		}
	}

	[Fact]
	public void AnswerSummary_PrefersTheFirstAddressRecord()
	{
		DnsLookupResult both = RaceReplies.Address("192.0.2.10");
		Assert.Equal("192.0.2.10", ResolverRace.AnswerSummary(both));
	}

	[Fact]
	public void AnswerSummary_FallsBackToTheResponseCodeWhenThereIsNoAddress()
	{
		Assert.Equal("NOERROR", ResolverRace.AnswerSummary(RaceReplies.Code(0)));
		Assert.Equal("NXDOMAIN", ResolverRace.AnswerSummary(RaceReplies.Code(3)));
		Assert.Equal("SERVFAIL", ResolverRace.AnswerSummary(RaceReplies.Code(2)));
	}

	private static IPEndPoint Endpoint(int last) => new(IPAddress.Parse($"192.0.2.{last}"), 53);
}
