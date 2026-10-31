using System.Net;
using Moka.Network.Lookup;
using Moka.Network.Lookup.Mapping;

namespace Moka.Network.Tests.Mapping;

public sealed class MapTextTests
{
	[Fact]
	public void Render_WritesEveryViewThatHasRun()
	{
		MapViews views = new()
		{
			Name = "example.com",
			Type = (int)DnsRecordType.A,
			Walk = new DelegationWalkResult
			{
				Steps =
				[
					new DelegationStep
					{
						ServerName = "a.root-servers.net",
						Server = Replies.Root,
						Exchange = Replies.Exchange(Replies.Message()),
						Outcome = "referred example.com to the servers of com: a.gtld-servers.net",
						Kind = DelegationStepKind.Referral,
					},
				],
				ReachedAnswer = true,
				Summary = "a.iana-servers.net answered with one record of the A kind asked for.",
			},
			Chain = new CnameChainResult
			{
				Hops =
				[
					new CnameHop
					{
						Name = "example.com",
						Records = [Replies.Record("example.com", DnsRecordType.CNAME, "www.example.com")],
						Server = Replies.Zone,
						Elapsed = TimeSpan.FromMilliseconds(18),
					},
					new CnameHop
					{
						Name = "www.example.com",
						Records = [Replies.Record("www.example.com", DnsRecordType.A, "93.184.216.34")],
						Server = Replies.Zone,
						Elapsed = TimeSpan.FromMilliseconds(21),
					},
				],
				FinalName = "www.example.com",
				Outcome = CnameChainOutcome.Complete,
				Summary = "www.example.com has one A record; the chain ends here.",
			},
			Matrix = new ResolverMatrixResult
			{
				Rows =
				[
					new ResolverMatrixRow
					{
						Resolver = "Cloudflare 1.1.1.1",
						Answered = true,
						Answer = "A 93.184.216.34",
						Code = "NOERROR",
						Elapsed = TimeSpan.FromMilliseconds(18),
						Failure = null,
						Fingerprint = "NOERROR|1 93.184.216.34",
					},
					new ResolverMatrixRow
					{
						Resolver = "Google 8.8.8.8",
						Answered = false,
						Answer = null,
						Code = null,
						Elapsed = TimeSpan.Zero,
						Failure = "8.8.8.8:53 did not answer within 3 s.",
						Fingerprint = "",
					},
				],
				Disagreements = ["Cloudflare 1.1.1.1 says NOERROR with A 93.184.216.34, Google 8.8.8.8 says nothing."],
			},
			SweepPrefix = "192.168.1.0/24",
			Sweep = new PtrSweepResult
			{
				Items =
				[
					new PtrSweepItem(IPAddress.Parse("192.168.1.1"), "router.example.lan"),
					new PtrSweepItem(IPAddress.Parse("192.168.1.2"), null),
				],
			},
		};

		string text = MapText.Render(views);

		Assert.StartsWith("DNS map of example.com, A records", text, StringComparison.Ordinal);
		Assert.Contains("Delegation, asked of the roots:", text, StringComparison.Ordinal);
		Assert.Contains("  1. a.root-servers.net (198.41.0.4:53)", text, StringComparison.Ordinal);
		Assert.Contains("referred example.com to the servers of com", text, StringComparison.Ordinal);
		Assert.Contains("CNAME chain:", text, StringComparison.Ordinal);
		Assert.Contains("    CNAME www.example.com", text, StringComparison.Ordinal);
		Assert.Contains("Resolvers, the same question:", text, StringComparison.Ordinal);
		Assert.Contains("  Cloudflare 1.1.1.1: NOERROR, A 93.184.216.34, 18 ms", text, StringComparison.Ordinal);
		Assert.Contains("  Google 8.8.8.8: could not be asked — 8.8.8.8:53 did not answer", text, StringComparison.Ordinal);
		Assert.Contains("  ! Cloudflare 1.1.1.1 says", text, StringComparison.Ordinal);
		Assert.Contains("PTR sweep of 192.168.1.0/24:", text, StringComparison.Ordinal);
		Assert.Contains("  192.168.1.1  router.example.lan", text, StringComparison.Ordinal);
		Assert.Contains("1 name, 1 address with no name", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Render_WritesASweepAloneWithoutANameHeader()
	{
		MapViews views = new()
		{
			Name = "",
			Type = 0,
			SweepPrefix = "10.0.0.0/28",
			Sweep = new PtrSweepResult { Items = [new PtrSweepItem(IPAddress.Parse("10.0.0.1"), "printer.example.lan")] },
		};

		string text = MapText.Render(views);

		Assert.StartsWith("PTR sweep of 10.0.0.0/28:", text, StringComparison.Ordinal);
		Assert.DoesNotContain("DNS map of", text, StringComparison.Ordinal);
		Assert.DoesNotContain("Delegation", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Render_SkipsTheViewsThatHaveNotRun()
	{
		string text = MapText.Render(new MapViews { Name = "example.com", Type = (int)DnsRecordType.AAAA });

		Assert.Equal("DNS map of example.com, AAAA records", text);
	}
}
