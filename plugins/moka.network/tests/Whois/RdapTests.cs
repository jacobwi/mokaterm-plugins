using Moka.Network.Targets;
using Moka.Network.Whois;

namespace Moka.Network.Tests.Whois;

/// <summary>The RDAP fallback against canned answers, the way the whois tests fake port 43; no HTTPS is opened.</summary>
public sealed class RdapTests
{
	private const string Bootstrap = """
		{
			"description": "RDAP bootstrap file for DNS",
			"publication": "2026-10-28T22:00:04Z",
			"services": [
				[["com", "net"], ["https://rdap.verisign.com/com/v1/", "https://rdap.verisign.com/net/v1/"]],
				[["de"], ["https://rdap.denic.de/domain/"]],
				[["kg"], ["http://rdap.cctld.kg/"]],
				"an entry that is not one"
			]
		}
		""";

	private const string RegistryAnswer = """
		{
			"objectClassName": "domain",
			"handle": "1264983250_DOMAIN_COM-VRSN",
			"ldhName": "GITHUB.COM",
			"links": [
				{"value": "https://rdap.verisign.com/com/v1/domain/github.com", "rel": "self", "href": "https://rdap.verisign.com/com/v1/domain/github.com"},
				{"value": "https://rdap.verisign.com/com/v1/domain/github.com", "rel": "related", "href": "https://rdap.markmonitor.com/rdap/domain/GITHUB.COM"}
			],
			"status": ["client delete prohibited", "client transfer prohibited"],
			"entities": [
				{"objectClassName": "entity", "handle": "292", "roles": ["registrar"],
				 "publicIds": [{"type": "IANA Registrar ID", "identifier": "292"}],
				 "vcardArray": ["vcard", [["version", {}, "text", "4.0"], ["fn", {}, "text", "MarkMonitor Inc."]]]}
			],
			"events": [
				{"eventAction": "registration", "eventDate": "2007-10-09T18:20:50Z"},
				{"eventAction": "expiration", "eventDate": "2028-10-09T18:20:50Z"},
				{"eventAction": "last changed", "eventDate": "2026-09-07T09:22:52Z"},
				{"eventAction": "last update of RDAP database", "eventDate": "2026-10-31T17:26:44Z"}
			],
			"nameservers": [
				{"objectClassName": "nameserver", "ldhName": "DNS1.P08.NSONE.NET"},
				{"objectClassName": "nameserver", "ldhName": "NS-421.AWSDNS-52.COM"}
			]
		}
		""";

	private const string RegistrarAnswer = """
		{
			"objectClassName": "domain",
			"ldhName": "github.com",
			"status": ["ok"],
			"entities": [
				{"roles": ["registrar"], "vcardArray": ["vcard", [["fn", {}, "text", "MarkMonitor Inc."]]]}
			],
			"events": [{"eventAction": "registration", "eventDate": "2007-10-09T18:20:50Z"}]
		}
		""";

	[Fact]
	public void Bootstrap_ReadsTheServicesAndSkipsWhatIsNotOne()
	{
		IReadOnlyList<RdapService> services = RdapBootstrap.Parse(Bootstrap);

		Assert.Equal(3, services.Count);
		Assert.Equal(["com", "net"], services[0].Tlds);
		Assert.Equal("https://rdap.verisign.com/com/v1/", services[0].Urls[0]);
	}

	[Fact]
	public void Bootstrap_FindsTheRegistryForATopLevelDomain()
	{
		IReadOnlyList<RdapService> services = RdapBootstrap.Parse(Bootstrap);

		Assert.Equal(services[0], RdapBootstrap.Find(services, "com"));
		Assert.Equal(services[0], RdapBootstrap.Find(services, "NET"));
		Assert.Equal(services[1], RdapBootstrap.Find(services, "de"));
		Assert.Null(RdapBootstrap.Find(services, "xyz"));
	}

	[Fact]
	public void Bootstrap_TakesOnlyHttpsUrls()
	{
		IReadOnlyList<RdapService> services = RdapBootstrap.Parse(Bootstrap);

		Assert.Equal("https://rdap.denic.de/domain/", services[1].HttpsUrl);
		Assert.Null(services[2].HttpsUrl);
	}

	[Theory]
	[InlineData("https://rdap.verisign.com/com/v1/", "example.com", "https://rdap.verisign.com/com/v1/domain/example.com")]
	[InlineData("https://rdap.denic.de/domain/", "denic.de", "https://rdap.denic.de/domain/denic.de")]
	[InlineData("https://rdap.example.org", "example.org", "https://rdap.example.org/domain/example.org")]
	public void DomainRequest_AppendsThePathTheBaseUrlLeavesOut(string baseUrl, string domain, string expected) =>
		Assert.Equal(expected, RdapBootstrap.DomainRequest(baseUrl, domain));

	[Fact]
	public void Read_TakesTheFactsOutOfAnAnswer()
	{
		(RdapDomain domain, string? referral) = RdapReader.Read(RegistryAnswer);

		Assert.Equal("GITHUB.COM", domain.Name);
		Assert.Equal("1264983250_DOMAIN_COM-VRSN", domain.Handle);
		Assert.Equal(["client delete prohibited", "client transfer prohibited"], domain.Statuses);
		Assert.Equal("2007-10-09T18:20:50Z", domain.Registered);
		Assert.Equal("2026-09-07T09:22:52Z", domain.Updated);
		Assert.Equal("2028-10-09T18:20:50Z", domain.Expires);
		Assert.Equal("MarkMonitor Inc.", domain.Registrar);
		Assert.Equal("292", domain.RegistrarId);
		Assert.Equal(["DNS1.P08.NSONE.NET", "NS-421.AWSDNS-52.COM"], domain.Nameservers);
		Assert.Equal("https://rdap.markmonitor.com/rdap/domain/GITHUB.COM", referral);
	}

	[Fact]
	public void Read_TakesTheReferralFromTheRegistrarEntityWhenItCarriesOne()
	{
		const string answer = """
			{
				"ldhName": "example.io",
				"entities": [
					{"roles": ["registrar"], "links": [
						{"rel": "related", "href": "https://rdap.nic.io/domain/example.io"}
					]}
				]
			}
			""";

		Assert.Equal("https://rdap.nic.io/domain/example.io", RdapReader.Read(answer).Referral);
	}

	[Fact]
	public void Read_ReturnsNoReferralWhenTheAnswerNamesNone()
	{
		const string answer = """
			{
				"ldhName": "example.io",
				"links": [{"rel": "self", "href": "https://rdap.nic.io/domain/example.io"}],
				"entities": [{"roles": ["registrar"], "vcardArray": ["vcard", [["fn", {}, "text", "Example Inc."]]]}]
			}
			""";

		(RdapDomain domain, string? referral) = RdapReader.Read(answer);

		Assert.Null(referral);
		Assert.Equal("Example Inc.", domain.Registrar);
	}

	[Fact]
	public void Read_LeavesEveryFactUnsetInAnAnswerWithoutThem()
	{
		(RdapDomain domain, string? referral) = RdapReader.Read("{}");

		Assert.Equal("", domain.Name);
		Assert.Null(domain.Handle);
		Assert.Empty(domain.Statuses);
		Assert.Null(domain.Registered);
		Assert.Null(domain.Registrar);
		Assert.Null(domain.RegistrarId);
		Assert.Empty(domain.Nameservers);
		Assert.Null(referral);
	}

	[Fact]
	public async Task Lookup_BootstrapsOnceThenAsksRegistryAndRegistrar()
	{
		FakeTransport transport = new(new Dictionary<string, RdapReply>
		{
			[RdapBootstrap.Url] = new(200, Bootstrap),
			["https://rdap.verisign.com/com/v1/domain/github.com"] = new(200, RegistryAnswer),
			["https://rdap.markmonitor.com/rdap/domain/GITHUB.COM"] = new(200, RegistrarAnswer),
		});
		using RdapClient client = new(transport);

		RdapResult first = await client.LookupDomainAsync("github.com", TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		Assert.Equal("github.com", first.Query);
		Assert.Equal("rdap.verisign.com", first.Server);
		Assert.Equal("rdap.markmonitor.com", first.RegistrarServer);
		Assert.Equal("MarkMonitor Inc.", first.Domain.Registrar);
		Assert.Equal("292", first.Domain.RegistrarId);
		Assert.Equal(["client delete prohibited", "client transfer prohibited"], first.Domain.Statuses);
		Assert.Equal(RegistryAnswer, first.RegistryJson);
		Assert.Equal(RegistrarAnswer, first.RegistrarJson);
		Assert.Null(first.Note);

		await client.LookupDomainAsync("github.com", TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		// The bootstrap list is kept for the app run; the second lookup asks the two servers again and no one else.
		Assert.Equal(5, transport.Requests.Count);
		Assert.Equal(1, transport.Requests.Count(url => url == RdapBootstrap.Url));
	}

	[Fact]
	public async Task Lookup_SaysPlainlyWhenTheBootstrapFails()
	{
		FakeTransport transport = new([]);
		using RdapClient client = new(transport);

		ProbeException error = await Assert.ThrowsAsync<ProbeException>(
			() => client.LookupDomainAsync("example.com", TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

		Assert.Contains("bootstrap", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Lookup_SaysPlainlyWhenNoRegistryIsListedForTheDomain()
	{
		FakeTransport transport = new(new Dictionary<string, RdapReply> { [RdapBootstrap.Url] = new(200, Bootstrap) });
		using RdapClient client = new(transport);

		ProbeException error = await Assert.ThrowsAsync<ProbeException>(
			() => client.LookupDomainAsync("example.xyz", TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

		Assert.Contains("names no HTTPS server for .xyz", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Lookup_ReportsADomainTheRegistryHasNoRecordFor()
	{
		FakeTransport transport = new(new Dictionary<string, RdapReply>
		{
			[RdapBootstrap.Url] = new(200, Bootstrap),
			["https://rdap.verisign.com/com/v1/domain/no-such-domain.com"] = new(404, "{\"errorCode\":404}"),
		});
		using RdapClient client = new(transport);

		ProbeException error = await Assert.ThrowsAsync<ProbeException>(
			() => client.LookupDomainAsync("no-such-domain.com", TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

		Assert.Contains("has no RDAP record for no-such-domain.com", error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Lookup_KeepsTheRegistryAnswerWhenTheReferralFails()
	{
		FakeTransport transport = new(new Dictionary<string, RdapReply>
		{
			[RdapBootstrap.Url] = new(200, Bootstrap),
			["https://rdap.verisign.com/com/v1/domain/github.com"] = new(200, RegistryAnswer),
		});
		using RdapClient client = new(transport);

		RdapResult result = await client.LookupDomainAsync("github.com", TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		Assert.Null(result.RegistrarJson);
		Assert.Null(result.RegistrarServer);
		Assert.Equal("MarkMonitor Inc.", result.Domain.Registrar);
		Assert.Contains("rdap.markmonitor.com", result.Note, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Lookup_DoesNotFollowAReferralBackToTheSameServer()
	{
		const string answer = """
			{
				"ldhName": "example.de",
				"links": [{"rel": "related", "href": "https://rdap.denic.de/domain/example.de"}]
			}
			""";
		FakeTransport transport = new(new Dictionary<string, RdapReply>
		{
			[RdapBootstrap.Url] = new(200, Bootstrap),
			["https://rdap.denic.de/domain/example.de"] = new(200, answer),
		});
		using RdapClient client = new(transport);

		RdapResult result = await client.LookupDomainAsync("example.de", TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		Assert.Equal(2, transport.Requests.Count);
		Assert.Null(result.RegistrarServer);
		Assert.Null(result.Note);
	}

	/// <summary>Answers from memory by url, the way the whois tests fake port 43.</summary>
	private sealed class FakeTransport(Dictionary<string, RdapReply> answers) : IRdapTransport
	{
		public List<string> Requests { get; } = [];

		public Task<RdapReply> GetAsync(string url, TimeSpan timeout, CancellationToken cancellationToken)
		{
			Requests.Add(url);
			return answers.TryGetValue(url, out RdapReply? reply)
				? Task.FromResult(reply)
				: throw new ProbeException($"{url}: no such server.");
		}
	}
}
