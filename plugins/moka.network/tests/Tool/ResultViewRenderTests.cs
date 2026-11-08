using System.Net;
using Moka.Network.Echo;
using Moka.Network.Lookup;
using Moka.Network.Lookup.Mapping;
using Moka.Network.Ports;
using Moka.Network.Tests.Lookup;
using Moka.Network.Tool;
using Moka.Network.Whois;
using Mokaterm.Abstractions.Platform;
using Mokaterm.Plugin.TestHost;

namespace Moka.Network.Tests.Tool;

/// <summary>
/// The result views with answers the tests build themselves, since nothing here may reach the network: what each one
/// shows once a run came back, its tiles, its tables and its charts.
/// </summary>
public sealed class ResultViewRenderTests
{
	[Fact]
	public async Task DnsAnswer_ShowsTheTilesTheFlagsTheRecordsAndTheTextCopy()
	{
		DnsLookupResult plain = RaceReplies.Address("192.0.2.10");
		DnsLookupResult result = plain with
		{
			Exchange = plain.Exchange with
			{
				OverTcp = true,
				Message = plain.Exchange.Message with { Header = plain.Exchange.Message.Header with { AuthenticData = true } },
			},
		};

		string html = await RenderAsync<DnsAnswerView>(new()
		{
			[nameof(DnsAnswerView.Result)] = result,
			[nameof(DnsAnswerView.Note)] = "192.0.2.10 is an address, so this asked for its PTR record.",
			[nameof(DnsAnswerView.AskedType)] = (int)DnsRecordType.A,
		});

		Assert.Contains("NOERROR", html, StringComparison.Ordinal);
		Assert.Contains("192.0.2.53:53", html, StringComparison.Ordinal);
		Assert.Contains("5.0 ms", html, StringComparison.Ordinal);
		Assert.Contains("DNSSEC validated", html, StringComparison.Ordinal);
		Assert.Contains("Over TCP", html, StringComparison.Ordinal);
		Assert.Contains("asked for its PTR record", html, StringComparison.Ordinal);
		Assert.Contains("192.0.2.10", html, StringComparison.Ordinal);
		Assert.Contains("5m", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Answer\"", html, StringComparison.Ordinal);
		Assert.Contains("Copy the answer as text", html, StringComparison.Ordinal);
		Assert.Contains(";; ANSWER", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task DnsAnswer_ForAnEmptyNoErrorAnswer_SaysTheNameHasNoRecordsOfThatType()
	{
		string html = await RenderAsync<DnsAnswerView>(new()
		{
			[nameof(DnsAnswerView.Result)] = RaceReplies.Code(0),
			[nameof(DnsAnswerView.AskedType)] = (int)DnsRecordType.AAAA,
		});

		Assert.Contains("The name exists, but has no AAAA records.", html, StringComparison.Ordinal);
		Assert.DoesNotContain("aria-label=\"Answer\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ResolverRace_DrawsTheTimesAsBars()
	{
		IReadOnlyList<ResolverRaceResult> results =
		[
			new(new ResolverRaceEntry("Cloudflare 1.1.1.1", [new(IPAddress.Parse("1.1.1.1"), 53)]), RaceReplies.Address("192.0.2.10"), null, TimeSpan.FromMilliseconds(12)),
			new(new ResolverRaceEntry("This machine's DNS", []), null, "There is no DNS server to ask.", TimeSpan.FromMilliseconds(1)),
		];

		string html = await RenderAsync<Components.ResolverRaceTable>(new()
		{
			[nameof(Components.ResolverRaceTable.Results)] = results,
			[nameof(Components.ResolverRaceTable.Question)] = "example.com A",
		});

		Assert.Contains("example.com A", html, StringComparison.Ordinal);
		Assert.Contains("moka-barchart", html, StringComparison.Ordinal);
		Assert.Contains(">Cloudflare<", html, StringComparison.Ordinal);
		Assert.DoesNotContain(">This machine<", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Map_DrawsTheDelegationTheChainTheResolversAndTheSweep()
	{
		DnsExchange answered = RaceReplies.Address("192.0.2.10").Exchange;
		MapViews views = new()
		{
			Name = "www.example.com",
			Type = (int)DnsRecordType.A,
			Walk = new DelegationWalkResult
			{
				Steps =
				[
					new DelegationStep { ServerName = "a.root-servers.net", Server = new IPEndPoint(IPAddress.Parse("198.41.0.4"), 53), Exchange = answered, Outcome = "referred www.example.com to the servers of com", Kind = DelegationStepKind.Referral },
					new DelegationStep { ServerName = "a.gtld-servers.net", Server = new IPEndPoint(IPAddress.Parse("192.5.6.30"), 53), Exchange = null, Outcome = "never answered", Kind = DelegationStepKind.Failed },
					new DelegationStep { ServerName = "b.gtld-servers.net", Server = new IPEndPoint(IPAddress.Parse("192.33.14.30"), 53), Exchange = answered, Outcome = "answered with 1 A record", Kind = DelegationStepKind.Answer },
				],
				ReachedAnswer = true,
				Summary = "b.gtld-servers.net answered after 3 servers.",
			},
			Chain = new CnameChainResult
			{
				Hops =
				[
					new CnameHop { Name = "www.example.com", Records = [new DnsRecord { Name = "www.example.com", Type = (int)DnsRecordType.CNAME, Class = 1, Ttl = 60, Data = "edge.example.net" }], Server = new IPEndPoint(IPAddress.Parse("192.0.2.53"), 53), Elapsed = TimeSpan.FromMilliseconds(4) },
					new CnameHop { Name = "edge.example.net", Records = [new DnsRecord { Name = "edge.example.net", Type = (int)DnsRecordType.A, Class = 1, Ttl = 60, Data = "192.0.2.80" }], Server = new IPEndPoint(IPAddress.Parse("192.0.2.53"), 53), Elapsed = TimeSpan.FromMilliseconds(6) },
				],
				FinalName = "edge.example.net",
				Outcome = CnameChainOutcome.Complete,
				Summary = "edge.example.net has 1 A record; the chain ends here.",
			},
			Matrix = new ResolverMatrixResult
			{
				Rows =
				[
					new ResolverMatrixRow { Resolver = "Cloudflare 1.1.1.1", Answered = true, Answer = "A 192.0.2.80", Code = "NOERROR", Elapsed = TimeSpan.FromMilliseconds(9), Failure = null, Fingerprint = "a" },
					new ResolverMatrixRow { Resolver = "Quad9 9.9.9.9", Answered = false, Answer = null, Code = null, Elapsed = TimeSpan.Zero, Failure = "9.9.9.9:53 did not answer.", Fingerprint = "" },
				],
				Disagreements = ["Quad9 9.9.9.9 gave no answer."],
			},
			SweepPrefix = "192.0.2.0/30",
			Sweep = new PtrSweepResult
			{
				Items =
				[
					new PtrSweepItem(IPAddress.Parse("192.0.2.1"), "gw.example.com"),
					new PtrSweepItem(IPAddress.Parse("192.0.2.2"), null),
				],
			},
		};

		string html = await RenderAsync<MapResultView>(new() { [nameof(MapResultView.Views)] = views });

		Assert.Contains("moka-chain-check", html, StringComparison.Ordinal);
		Assert.Contains("a.root-servers.net", html, StringComparison.Ordinal);
		Assert.Contains("Referred www.example.com to the servers of com", html, StringComparison.Ordinal);
		Assert.Contains("b.gtld-servers.net answered after 3 servers.", html, StringComparison.Ordinal);
		Assert.Contains("moka-timeline", html, StringComparison.Ordinal);
		Assert.Contains("edge.example.net", html, StringComparison.Ordinal);
		Assert.Contains("9.9.9.9:53 did not answer.", html, StringComparison.Ordinal);
		Assert.Contains("Quad9 9.9.9.9 gave no answer.", html, StringComparison.Ordinal);
		Assert.Contains("moka-barchart", html, StringComparison.Ordinal);
		Assert.Contains("gw.example.com", html, StringComparison.Ordinal);
		Assert.Contains("1 of 2 addresses are silent", html, StringComparison.Ordinal);
		Assert.Contains("moka-donut", html, StringComparison.Ordinal);
	}

	[Fact]
	public void Map_AServerThatNeverAnswered_BreaksTheChainOnlyWhenNothingAfterItDid()
	{
		IPEndPoint server = new(IPAddress.Parse("192.0.2.1"), 53);
		DelegationStep failed = new() { ServerName = "ns1", Server = server, Outcome = "never answered", Kind = DelegationStepKind.Failed };
		DelegationStep answer = new() { ServerName = "ns2", Server = server, Exchange = RaceReplies.Address("192.0.2.10").Exchange, Outcome = "answered", Kind = DelegationStepKind.Answer };

		IReadOnlyList<Moka.Red.Primitives.ChainCheck.MokaChainStep> wentOn = MapResultView.DelegationSteps(new DelegationWalkResult { Steps = [failed, answer], ReachedAnswer = true, Summary = "" });
		IReadOnlyList<Moka.Red.Primitives.ChainCheck.MokaChainStep> stopped = MapResultView.DelegationSteps(new DelegationWalkResult { Steps = [answer with { Kind = DelegationStepKind.Referral }, failed], ReachedAnswer = false, Summary = "" });

		Assert.Equal(Moka.Red.Primitives.ChainCheck.MokaChainTone.Warn, wentOn[0].Tone);
		Assert.Equal(Moka.Red.Primitives.ChainCheck.MokaChainTone.Ok, wentOn[1].Tone);
		Assert.Equal(Moka.Red.Primitives.ChainCheck.MokaChainTone.Fail, stopped[1].Tone);
		Assert.Equal("192.0.2.1:53, 5.0 ms", wentOn[1].Detail);
	}

	[Fact]
	public async Task Ping_ShowsTheSummaryTheRoundTripLineAndTheReplies()
	{
		PingStatistics stats = new();
		List<PingRow> rows = [];
		EchoReply[] replies =
		[
			new(EchoStatus.Reply, IPAddress.Parse("192.0.2.10"), 12.4),
			EchoReply.TimedOut,
			new(EchoStatus.Reply, IPAddress.Parse("192.0.2.10"), 8.3),
		];
		for (int index = 0; index < replies.Length; index++)
		{
			stats.Add(replies[index]);
			rows.Add(new PingRow(index + 1, replies[index]));
		}

		string html = await RenderAsync<PingResultView>(new()
		{
			[nameof(PingResultView.Target)] = "Pinging example.com (192.0.2.10) with 32 bytes of data",
			[nameof(PingResultView.Stats)] = stats,
			[nameof(PingResultView.Rows)] = rows,
			[nameof(PingResultView.Planned)] = 4,
			[nameof(PingResultView.Live)] = true,
		});

		Assert.Contains("Pinging example.com (192.0.2.10)", html, StringComparison.Ordinal);
		Assert.Contains("2 of 3", html, StringComparison.Ordinal);
		Assert.Contains("33.3%", html, StringComparison.Ordinal);
		Assert.Contains("8.3 ms", html, StringComparison.Ordinal);
		Assert.Contains("12 ms", html, StringComparison.Ordinal);
		Assert.Contains("moka-linechart", html, StringComparison.Ordinal);
		Assert.Contains("timed out", html, StringComparison.Ordinal);
		Assert.Contains("aria-label=\"Replies\"", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Ping_ShowsWhatThePathMtuSearchFound()
	{
		string html = await RenderAsync<PingResultView>(new()
		{
			[nameof(PingResultView.Target)] = "Finding the path MTU to 192.0.2.10",
			[nameof(PingResultView.Stats)] = new PingStatistics(),
			[nameof(PingResultView.Rows)] = Array.Empty<PingRow>(),
			[nameof(PingResultView.Mtu)] = new PathMtuResult(1452, 11),
		});

		Assert.Contains("Packets of 1452 bytes pass and 1453 bytes do not", html, StringComparison.Ordinal);
		Assert.Contains("1480 bytes", html, StringComparison.Ordinal);
		Assert.Contains("DF-set probes", html, StringComparison.Ordinal);
		Assert.DoesNotContain("moka-linechart", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Port_ShowsEachStateTheServicesTheTimesAndTheGreeting()
	{
		IPAddress address = IPAddress.Parse("192.0.2.10");
		PortRow[] rows =
		[
			new(22, new PortResult { Port = 22, State = PortState.Open, Endpoint = new(address, 22), Elapsed = TimeSpan.FromMilliseconds(14), Greeting = "SSH-2.0-OpenSSH_9.6" }),
			new(443, new PortResult { Port = 443, State = PortState.Refused, Endpoint = new(address, 443), Elapsed = TimeSpan.FromMilliseconds(3) }),
			new(3000, new PortResult { Port = 3000, State = PortState.TimedOut, Endpoint = new(address, 3000), Elapsed = TimeSpan.FromSeconds(3) }),
			new(8080),
		];

		string html = await RenderAsync<PortResultView>(new()
		{
			[nameof(PortResultView.Target)] = "Connecting to example.com at 192.0.2.10",
			[nameof(PortResultView.Rows)] = rows,
			[nameof(PortResultView.CustomOnly)] = new HashSet<int> { 3000 },
			[nameof(PortResultView.GreetingAsked)] = true,
		});

		Assert.Contains("open", html, StringComparison.Ordinal);
		Assert.Contains("refused", html, StringComparison.Ordinal);
		Assert.Contains("no answer", html, StringComparison.Ordinal);
		Assert.Contains("checking", html, StringComparison.Ordinal);
		Assert.Contains("ssh", html, StringComparison.Ordinal);
		Assert.Contains("custom", html, StringComparison.Ordinal);
		Assert.Contains("192.0.2.10:22", html, StringComparison.Ordinal);
		Assert.Contains("14 ms", html, StringComparison.Ordinal);
		Assert.Contains("moka-barchart", html, StringComparison.Ordinal);
		Assert.Contains("SSH-2.0-OpenSSH_9.6", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Trace_ShowsTheHopsTheirNamesTheBarsAndHowItEnded()
	{
		IPAddress router = IPAddress.Parse("10.0.0.1");
		IPAddress destination = IPAddress.Parse("192.0.2.10");
		TraceHop[] hops =
		[
			new(1, [new(EchoStatus.TtlExpired, router, 0.8), new(EchoStatus.TtlExpired, router, 0.6), new(EchoStatus.TtlExpired, router, 0.7)]),
			new(2, [new(EchoStatus.TimedOut, null, null), new(EchoStatus.TimedOut, null, null), new(EchoStatus.TimedOut, null, null)]),
			new(3, [new(EchoStatus.Reply, destination, 11.2), new(EchoStatus.Reply, destination, 10.9), new(EchoStatus.Reply, destination, 11.5)]),
		];

		string html = await RenderAsync<TraceResultView>(new()
		{
			[nameof(TraceResultView.Target)] = "Tracing example.com (192.0.2.10), at most 30 hops",
			[nameof(TraceResultView.Hops)] = hops,
			[nameof(TraceResultView.Names)] = new Dictionary<IPAddress, string> { [router] = "gateway.lan" },
			[nameof(TraceResultView.End)] = TraceEnd.Reached,
			[nameof(TraceResultView.Destination)] = destination,
		});

		Assert.Contains("gateway.lan", html, StringComparison.Ordinal);
		Assert.Contains("no answer", html, StringComparison.Ordinal);
		Assert.Contains("0.6 ms", html, StringComparison.Ordinal);
		Assert.Contains("Destination", html, StringComparison.Ordinal);
		Assert.Contains("moka-barchart", html, StringComparison.Ordinal);
		Assert.Contains("Reached 192.0.2.10 in 3 hops.", html, StringComparison.Ordinal);
		Assert.Equal(10.9, TraceResultView.Best(hops[2]));
		Assert.Null(TraceResultView.Best(hops[1]));
	}

	[Fact]
	public async Task Whois_ShowsTheReferralChainAndEveryAnswer()
	{
		WhoisAnswer[] answers =
		[
			new() { Server = "whois.iana.org", Text = "refer: whois.verisign-grs.com", Bytes = 30, Elapsed = TimeSpan.FromMilliseconds(80), Referral = "whois.verisign-grs.com" },
			new() { Server = "whois.verisign-grs.com", Text = "Domain Name: EXAMPLE.COM", Bytes = 24, Elapsed = TimeSpan.FromMilliseconds(120), Cut = true },
			new() { Server = "whois.registrar.example", Text = "", Bytes = 0, Elapsed = TimeSpan.Zero, Error = "The connection was refused." },
		];

		string html = await RenderAsync<WhoisResultView>(new() { [nameof(WhoisResultView.Answers)] = answers });

		Assert.Contains("moka-chain-check", html, StringComparison.Ordinal);
		Assert.Contains("whois.iana.org &gt; whois.verisign-grs.com", html, StringComparison.Ordinal);
		Assert.Contains("refers on", html, StringComparison.Ordinal);
		Assert.Contains("cut at the size limit", html, StringComparison.Ordinal);
		Assert.Contains("Domain Name: EXAMPLE.COM", html, StringComparison.Ordinal);
		Assert.Contains("The connection was refused.", html, StringComparison.Ordinal);
		Assert.Contains("Copy what whois.verisign-grs.com said", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Whois_OverRdap_ShowsTheFactsTheDatesAndTheJson()
	{
		RdapResult rdap = new()
		{
			Query = "example.com",
			Server = "rdap.verisign.com",
			Domain = new RdapDomain
			{
				Name = "EXAMPLE.COM",
				Registrar = "Example Registrar",
				RegistrarId = "376",
				Statuses = ["client transfer prohibited"],
				Registered = "1995-08-14T04:00:00Z",
				Expires = "2027-08-13T04:00:00Z",
				Nameservers = ["a.iana-servers.net"],
			},
			RegistryJson = "{\"objectClassName\":\"domain\"}",
		};

		string html = await RenderAsync<WhoisResultView>(new()
		{
			[nameof(WhoisResultView.Rdap)] = rdap,
			[nameof(WhoisResultView.Note)] = "Port 43 gave nothing, so the registry was asked over RDAP instead.",
		});

		Assert.Contains("Example Registrar, IANA ID 376", html, StringComparison.Ordinal);
		Assert.Contains("client transfer prohibited", html, StringComparison.Ordinal);
		Assert.Contains("a.iana-servers.net", html, StringComparison.Ordinal);
		Assert.Contains("moka-timeline", html, StringComparison.Ordinal);
		Assert.Contains("1995", html, StringComparison.Ordinal);
		Assert.Contains("Port 43", html, StringComparison.Ordinal);
		Assert.Contains("objectClassName", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Wake_SaysWhatWentWhere()
	{
		WakeSent sent = new("00:11:22:33:44:55", "192.168.1.255:9", 102, "FF FF FF FF FF FF 00 11", new DateTimeOffset(2026, 11, 8, 9, 30, 15, TimeSpan.Zero));

		string html = await RenderAsync<WakeResultView>(new() { [nameof(WakeResultView.Sent)] = sent });

		Assert.Contains("Magic packet sent", html, StringComparison.Ordinal);
		Assert.Contains("192.168.1.255:9", html, StringComparison.Ordinal);
		Assert.Contains("00:11:22:33:44:55", html, StringComparison.Ordinal);
		Assert.Contains("102 bytes", html, StringComparison.Ordinal);
		Assert.Contains("FF FF FF FF FF FF 00 11", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Subnets_WorksOutTheStartingPrefixIntoTilesAndFacts()
	{
		// Addresses and masks may wrap after each dot, through a <wbr> the test reads past.
		string html = (await RenderAsync<SubnetSection>([])).Replace("<wbr />", "", StringComparison.Ordinal).Replace("<wbr>", "", StringComparison.Ordinal);

		Assert.Contains("192.168.1.0/24", html, StringComparison.Ordinal);
		Assert.Contains("254", html, StringComparison.Ordinal);
		Assert.Contains("255.255.255.0", html, StringComparison.Ordinal);
		Assert.Contains("11111111.11111111.11111111.00000000", html, StringComparison.Ordinal);
		Assert.Contains("192.168.1.255", html, StringComparison.Ordinal);
		Assert.Contains("FIRST HOST", html, StringComparison.Ordinal);
		Assert.Contains("Copy the network address", html, StringComparison.Ordinal);
		Assert.Contains("Private", html, StringComparison.Ordinal);
	}

	private static Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters)
		where TComponent : Microsoft.AspNetCore.Components.IComponent =>
		StaticRender.RenderAsync<TComponent>(parameters, HostKind.Desktop);
}
