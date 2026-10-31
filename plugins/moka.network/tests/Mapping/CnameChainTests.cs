using System.Globalization;
using Moka.Network.Lookup;
using Moka.Network.Lookup.Mapping;

namespace Moka.Network.Tests.Mapping;

public sealed class CnameChainTests
{
	private const int A = (int)DnsRecordType.A;

	[Fact]
	public async Task RunAsync_FollowsTheChainToTheFinalRecords()
	{
		CnameChain.Lookup lookup = (name, type, _) => Task.FromResult(Replies.LookupResult(name == "example.com"
			? Replies.Message(answers: [Replies.Record("example.com", DnsRecordType.CNAME, "www.example.com")])
			: Replies.Message(answers:
			[
				Replies.Record("www.example.com", DnsRecordType.A, "93.184.216.34"),
				Replies.Record("www.example.com", DnsRecordType.A, "93.184.216.35"),
			], authoritative: true)));

		CnameChainResult chain = await CnameChain.RunAsync(lookup, "example.com", A, CancellationToken.None);

		Assert.Equal(CnameChainOutcome.Complete, chain.Outcome);
		Assert.Equal(2, chain.Hops.Count);
		Assert.Equal("example.com", chain.Hops[0].Name);
		Assert.Equal("www.example.com", chain.Hops[1].Name);
		DnsRecord cname = Assert.Single(chain.Hops[0].Records);
		Assert.Equal("CNAME", cname.TypeName);
		Assert.Equal("www.example.com", cname.Data);
		Assert.Equal(2, chain.Hops[1].Records.Count);
		Assert.Equal("www.example.com", chain.FinalName);
		Assert.Contains("has 2 A records", chain.Summary, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_ASingleNameWithNoCnameIsOneHop()
	{
		CnameChain.Lookup lookup = (_, _, _) => Task.FromResult(Replies.LookupResult(
			Replies.Message(answers: [Replies.Record("example.com", DnsRecordType.A, "93.184.216.34")], authoritative: true)));

		CnameChainResult chain = await CnameChain.RunAsync(lookup, "example.com", A, CancellationToken.None);

		Assert.Equal(CnameChainOutcome.Complete, chain.Outcome);
		DnsRecord record = Assert.Single(Assert.Single(chain.Hops).Records);
		Assert.Equal("A", record.TypeName);
	}

	[Fact]
	public async Task RunAsync_WordsACnameThatPointsAtItself()
	{
		CnameChain.Lookup lookup = (_, _, _) => Task.FromResult(Replies.LookupResult(
			Replies.Message(answers: [Replies.Record("loop.example.com", DnsRecordType.CNAME, "loop.example.com")])));

		CnameChainResult chain = await CnameChain.RunAsync(lookup, "loop.example.com", A, CancellationToken.None);

		Assert.Equal(CnameChainOutcome.SelfReference, chain.Outcome);
		Assert.Single(chain.Hops);
		Assert.Contains("points at itself", chain.Summary, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_WordsAChainThatComesBackToAVisitedName()
	{
		CnameChain.Lookup lookup = (name, type, _) => Task.FromResult(Replies.LookupResult(
			Replies.Message(answers: [Replies.Record(name, DnsRecordType.CNAME, name == "a.example.com" ? "b.example.com" : "a.example.com")])));

		CnameChainResult chain = await CnameChain.RunAsync(lookup, "a.example.com", A, CancellationToken.None);

		Assert.Equal(CnameChainOutcome.Loop, chain.Outcome);
		Assert.Equal(2, chain.Hops.Count);
		Assert.Contains("came back to a.example.com", chain.Summary, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_CutsTheChainAtTenHops()
	{
		CnameChain.Lookup lookup = (name, type, _) => Task.FromResult(Replies.LookupResult(
			Replies.Message(answers: [Replies.Record(name, DnsRecordType.CNAME, $"hop{int.Parse(name[3..^12], CultureInfo.InvariantCulture) + 1}.example.com")])));

		CnameChainResult chain = await CnameChain.RunAsync(lookup, "hop0.example.com", A, CancellationToken.None);

		Assert.Equal(CnameChainOutcome.TooLong, chain.Outcome);
		Assert.Equal(CnameChain.MaxHops, chain.Hops.Count);
		Assert.Contains($"cut at {CnameChain.MaxHops} hops", chain.Summary, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_WordsAnNxdomainMidChain()
	{
		CnameChain.Lookup lookup = (name, type, _) => Task.FromResult(name == "example.com"
			? Replies.LookupResult(Replies.Message(answers: [Replies.Record("example.com", DnsRecordType.CNAME, "gone.example.com")]))
			: Replies.LookupResult(Replies.Message(rcode: 3), Replies.Tld));

		CnameChainResult chain = await CnameChain.RunAsync(lookup, "example.com", A, CancellationToken.None);

		Assert.Equal(CnameChainOutcome.Failed, chain.Outcome);
		Assert.Equal(2, chain.Hops.Count);
		Assert.Equal("gone.example.com", chain.FinalName);
		Assert.Contains("NXDOMAIN", chain.Summary, StringComparison.Ordinal);
		Assert.Contains("gone.example.com", chain.Summary, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RunAsync_WordsALookupThatFailsOutright()
	{
		CnameChain.Lookup lookup = (_, _, _) => throw new DnsLookupException("1.1.1.1:53 did not answer within 3 s.");

		CnameChainResult chain = await CnameChain.RunAsync(lookup, "example.com", A, CancellationToken.None);

		Assert.Equal(CnameChainOutcome.Failed, chain.Outcome);
		Assert.Empty(chain.Hops);
		Assert.Contains("did not answer within 3 s", chain.Summary, StringComparison.Ordinal);
	}
}
