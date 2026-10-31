using System.Net;
using Moka.Network.Lookup;
using Moka.Network.Lookup.Mapping;

namespace Moka.Network.Tests.Mapping;

public sealed class DelegationWalkTests
{
	private const int A = (int)DnsRecordType.A;

	[Fact]
	public async Task RunAsync_FollowsReferralsWithGlueToTheAnswer()
	{
		DelegationWalk.Query query = (server, name, type, _) => Task.FromResult(server.Equals(Replies.Root)
			? Replies.Exchange(Replies.Message(
				authority: [Replies.Record("com", DnsRecordType.NS, "a.gtld-servers.net")],
				additional: [Replies.Record("a.gtld-servers.net", DnsRecordType.A, "192.5.6.30")]),
				Replies.Root)
			: server.Equals(Replies.Tld)
				? Replies.Exchange(Replies.Message(
					authority: [Replies.Record("example.com", DnsRecordType.NS, "a.iana-servers.net")],
					additional: [Replies.Record("a.iana-servers.net", DnsRecordType.A, "199.43.135.53")]),
					Replies.Tld)
				: Replies.Exchange(Replies.Message(answers: [Replies.Record("example.com", DnsRecordType.A, "93.184.216.34")], authoritative: true), Replies.Zone));

		DelegationWalkResult walk = await DelegationWalk.RunAsync(query, "example.com", A, CancellationToken.None);

		Assert.True(walk.ReachedAnswer);
		Assert.Equal(3, walk.Steps.Count);
		Assert.Equal(DelegationStepKind.Referral, walk.Steps[0].Kind);
		Assert.Contains("referred example.com to the servers of com", walk.Steps[0].Outcome, StringComparison.Ordinal);
		Assert.Equal("a.gtld-servers.net", walk.Steps[1].ServerName);
		Assert.Equal(DelegationStepKind.Answer, walk.Steps[2].Kind);
		Assert.Contains("answered with one record", walk.Steps[2].Outcome, StringComparison.Ordinal);
		Assert.Contains("answered with one record", walk.Summary, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_AsksTheReferringServerForAnAddressWhenGlueIsMissing()
	{
		DelegationWalk.Query query = (server, name, type, _) => Task.FromResult(name == "a.gtld-servers.net"
			? Replies.Exchange(Replies.Message(answers: [Replies.Record("a.gtld-servers.net", DnsRecordType.A, "192.5.6.30")]), Replies.Root)
			: server.Equals(Replies.Root)
				? Replies.Exchange(Replies.Message(authority: [Replies.Record("com", DnsRecordType.NS, "a.gtld-servers.net")]), Replies.Root)
				: Replies.Exchange(Replies.Message(answers: [Replies.Record("example.com", DnsRecordType.A, "93.184.216.34")], authoritative: true), Replies.Tld));

		DelegationWalkResult walk = await DelegationWalk.RunAsync(query, "example.com", A, CancellationToken.None);

		Assert.True(walk.ReachedAnswer);
		Assert.Equal(3, walk.Steps.Count);
		Assert.Contains("had no glue for a.gtld-servers.net", walk.Steps[1].Outcome, StringComparison.Ordinal);
		Assert.Contains("192.5.6.30", walk.Steps[1].Outcome, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_StopsHonestlyWhenAMissingGlueCannotBeResolvedEither()
	{
		DelegationWalk.Query query = (server, name, type, _) => Task.FromResult(
			Replies.Exchange(Replies.Message(authority: [Replies.Record("com", DnsRecordType.NS, "a.gtld-servers.net")]), Replies.Root));

		DelegationWalkResult walk = await DelegationWalk.RunAsync(query, "example.com", A, CancellationToken.None);

		Assert.False(walk.ReachedAnswer);
		Assert.Single(walk.Steps);
		Assert.Contains("whose address neither the referral nor a.root-servers.net gives", walk.Summary, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_WordsAnNxdomainFromTheRoot()
	{
		DelegationWalk.Query query = (_, _, _, _) => Task.FromResult(
			Replies.Exchange(Replies.Message(rcode: 3), Replies.Root));

		DelegationWalkResult walk = await DelegationWalk.RunAsync(query, "no-such-tld-not-a-real-name", A, CancellationToken.None);

		Assert.False(walk.ReachedAnswer);
		Assert.Single(walk.Steps);
		Assert.Equal(DelegationStepKind.Failed, walk.Steps[0].Kind);
		Assert.Contains("The walk stopped at a.root-servers.net", walk.Summary, StringComparison.Ordinal);
		Assert.Contains("does not exist", walk.Summary, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_WordsAnNxdomainFromTheZoneServers()
	{
		DelegationWalk.Query query = (server, name, type, _) => Task.FromResult(server.Equals(Replies.Root)
			? Replies.Exchange(Replies.Message(
				authority: [Replies.Record("com", DnsRecordType.NS, "a.gtld-servers.net")],
				additional: [Replies.Record("a.gtld-servers.net", DnsRecordType.A, "192.5.6.30")]),
				Replies.Root)
			: Replies.Exchange(Replies.Message(rcode: 3), Replies.Tld));

		DelegationWalkResult walk = await DelegationWalk.RunAsync(query, "no-such.example.com", A, CancellationToken.None);

		Assert.False(walk.ReachedAnswer);
		Assert.Equal(2, walk.Steps.Count);
		Assert.Equal(DelegationStepKind.Failed, walk.Steps[1].Kind);
		Assert.Contains("The walk stopped at a.gtld-servers.net", walk.Summary, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_TriesEachRootWhenOneTimesOut()
	{
		int asked = 0;
		DelegationWalk.Query query = (server, name, type, _) =>
		{
			asked++;
			if (asked == 1)
			{
				throw new DnsLookupException($"{DnsClient.Describe(server)} did not answer within 3 s.");
			}

			return Task.FromResult(Replies.Exchange(Replies.Message(answers: [Replies.Record("example.com", DnsRecordType.A, "93.184.216.34")], authoritative: true), server));
		};

		DelegationWalkResult walk = await DelegationWalk.RunAsync(query, "example.com", A, CancellationToken.None);

		Assert.True(walk.ReachedAnswer);
		Assert.Equal(2, walk.Steps.Count);
		Assert.Equal(DelegationStepKind.Failed, walk.Steps[0].Kind);
		Assert.Equal("b.root-servers.net", walk.Steps[1].ServerName);
		Assert.Contains("did not answer within 3 s", walk.Steps[0].Outcome, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_SaysItCannotStartWhenEveryRootIsSilent()
	{
		DelegationWalk.Query query = (server, _, _, _) => throw new DnsLookupException($"{DnsClient.Describe(server)} did not answer within 3 s.");

		DelegationWalkResult walk = await DelegationWalk.RunAsync(query, "example.com", A, CancellationToken.None);

		Assert.False(walk.ReachedAnswer);
		Assert.Equal(DelegationWalk.Roots.Count, walk.Steps.Count);
		Assert.All(walk.Steps, step => Assert.Equal(DelegationStepKind.Failed, step.Kind));
		Assert.All(walk.Steps, step => Assert.Null(step.Exchange));
		Assert.Contains("No root server answered", walk.Summary, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_WordsATimeoutAtAReferredServer()
	{
		DelegationWalk.Query query = (server, name, type, _) => server.Equals(Replies.Root)
			? Task.FromResult(Replies.Exchange(Replies.Message(
				authority: [Replies.Record("com", DnsRecordType.NS, "a.gtld-servers.net")],
				additional: [Replies.Record("a.gtld-servers.net", DnsRecordType.A, "192.5.6.30")]),
				Replies.Root))
			: throw new DnsLookupException($"{DnsClient.Describe(server)} did not answer within 3 s.");

		DelegationWalkResult walk = await DelegationWalk.RunAsync(query, "example.com", A, CancellationToken.None);

		Assert.False(walk.ReachedAnswer);
		Assert.Equal(2, walk.Steps.Count);
		Assert.Equal(DelegationStepKind.Failed, walk.Steps[1].Kind);
		Assert.Contains("never replied", walk.Summary, StringComparison.Ordinal);
		Assert.Contains("did not answer within 3 s", walk.Steps[1].Outcome, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_GivesUpAtTheStepCapWhenReferralsNeverEnd()
	{
		int asked = 0;
		DelegationWalk.Query query = (server, name, type, _) => Task.FromResult(Replies.Exchange(Replies.Message(
			authority: [Replies.Record($"z{asked++}.example", DnsRecordType.NS, $"ns{asked}.example")],
			additional: [Replies.Record($"ns{asked}.example", DnsRecordType.A, $"192.0.2.{Math.Min(asked, 250)}")]),
			Replies.Root));

		DelegationWalkResult walk = await DelegationWalk.RunAsync(query, "example.com", A, CancellationToken.None);

		Assert.False(walk.ReachedAnswer);
		Assert.Equal(DelegationWalk.MaxSteps, walk.Steps.Count);
		Assert.Contains($"gave up after {DelegationWalk.MaxSteps} servers", walk.Summary, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_DetectsAReferralBackToAZoneAlreadyVisited()
	{
		DelegationWalk.Query query = (server, name, type, _) => Task.FromResult(server.Equals(Replies.Root)
			? Replies.Exchange(Replies.Message(
				authority: [Replies.Record("com", DnsRecordType.NS, "a.gtld-servers.net")],
				additional: [Replies.Record("a.gtld-servers.net", DnsRecordType.A, "192.5.6.30")]),
				Replies.Root)
			: Replies.Exchange(Replies.Message(
				authority: [Replies.Record("com", DnsRecordType.NS, "b.gtld-servers.net")],
				additional: [Replies.Record("b.gtld-servers.net", DnsRecordType.A, "192.5.6.31")]),
				Replies.Tld));

		DelegationWalkResult walk = await DelegationWalk.RunAsync(query, "example.com", A, CancellationToken.None);

		Assert.False(walk.ReachedAnswer);
		Assert.Equal(2, walk.Steps.Count);
		Assert.Contains("loops", walk.Summary, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_WordsNodataFromTheAuthoritativeServer()
	{
		DelegationWalk.Query query = (server, name, type, _) => Task.FromResult(server.Equals(Replies.Root)
			? Replies.Exchange(Replies.Message(
				authority: [Replies.Record("com", DnsRecordType.NS, "a.gtld-servers.net")],
				additional: [Replies.Record("a.gtld-servers.net", DnsRecordType.A, "192.5.6.30")]),
				Replies.Root)
			: Replies.Exchange(Replies.Message(authoritative: true), Replies.Tld));

		DelegationWalkResult walk = await DelegationWalk.RunAsync(query, "example.com", A, CancellationToken.None);

		Assert.False(walk.ReachedAnswer);
		Assert.Equal(2, walk.Steps.Count);
		Assert.Equal(DelegationStepKind.NoData, walk.Steps[1].Kind);
		Assert.Contains("exists but has no A records", walk.Summary, StringComparison.Ordinal);
	}
}
