using System.Net;
using Moka.Network.Echo;
using Moka.Network.Lookup;
using Moka.Network.Ports;
using Moka.Network.Whois;

namespace Moka.Network.Tests.Live;

/// <summary>
/// Every probe once against real servers, skipped unless MOKA_NETWORK_LIVE=1, the way Mokaterm gates its live protocol
/// tests: the rest of the suite never leaves the loopback address.
/// </summary>
public sealed class NetworkLiveTests
{
	private static readonly IPAddress Cloudflare = IPAddress.Parse("1.1.1.1");

	[Fact]
	public async Task Dns_AsksAPublicResolverOverUdpAndTcp()
	{
		SkipUnlessLive();
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		IReadOnlyList<IPEndPoint> servers = DnsResolvers.Servers(DnsResolverKind.Cloudflare, null, out _);

		DnsLookupResult a = await DnsLookup.RunAsync(servers, "example.com", (int)DnsRecordType.A, TimeSpan.FromSeconds(5), cancellationToken);
		DnsLookupResult txt = await DnsLookup.RunAsync(servers, "google.com", (int)DnsRecordType.TXT, TimeSpan.FromSeconds(5), cancellationToken);
		string? ptr = await DnsLookup.ReverseAsync(servers, Cloudflare, TimeSpan.FromSeconds(5), cancellationToken);

		Assert.Equal(0, a.Exchange.Message.ResponseCode);
		Assert.Contains(a.Exchange.Message.Answers, record => record.Type == (int)DnsRecordType.A);
		Assert.NotEmpty(txt.Exchange.Message.Answers);
		Assert.Equal("one.one.one.one", ptr);
		TestContext.Current.SendDiagnosticMessage($"TXT over {(txt.Exchange.OverTcp ? "TCP" : "UDP")}, {txt.Exchange.Message.Size} bytes");
	}

	[Fact]
	public async Task Dns_ReportsANameThatDoesNotExist()
	{
		SkipUnlessLive();
		IReadOnlyList<IPEndPoint> servers = DnsResolvers.Servers(DnsResolverKind.Google, null, out _);

		DnsLookupResult result = await DnsLookup.RunAsync(servers, "no-such-name.example", (int)DnsRecordType.A, TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		Assert.Equal("NXDOMAIN", result.Exchange.Message.ResponseCodeName);
	}

	[Fact]
	public async Task Whois_FollowsIanaToTheRegistry()
	{
		SkipUnlessLive();

		// Not example.com: IANA holds that one itself and refers nowhere.
		WhoisResult result = await WhoisClient.LookupAsync(TcpWhoisTransport.Instance, "github.com", TimeSpan.FromSeconds(15), _ => { }, TestContext.Current.CancellationToken);

		TestContext.Current.SendDiagnosticMessage(string.Join(" > ", result.Answers.Select(answer => $"{answer.Server} ({answer.Bytes} bytes{(answer.Error is null ? "" : ", " + answer.Error)})")));
		Assert.True(result.Answers.Count >= 2);
		Assert.Equal(WhoisClient.RootServer, result.Answers[0].Server);
		Assert.Equal("whois.verisign-grs.com", result.Answers[1].Server);
		Assert.Contains("GITHUB.COM", result.Answers[1].Text, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Ping_GetsReplies()
	{
		SkipUnlessLive();
		PingStatistics stats = new();
		PingPlan plan = new(Cloudflare, 3, PingPlan.MinInterval, new EchoRequest(EchoRequest.DefaultTtl, TimeSpan.FromSeconds(2), EchoRequest.DefaultPayload));

		await PingRunner.RunAsync(SystemPinger.Instance, plan, (_, reply) => stats.Add(reply), TimeProvider.System, TestContext.Current.CancellationToken);

		Assert.Equal(3, stats.Sent);
		Assert.True(stats.Received > 0);
	}

	[Fact]
	public async Task Trace_ReachesAPublicResolver()
	{
		SkipUnlessLive();
		List<TraceHop> hops = [];

		TraceEnd end = await Traceroute.RunAsync(SystemPinger.Instance, Cloudflare, new TraceOptions(30, 1, TimeSpan.FromSeconds(1)), hops.Add, TestContext.Current.CancellationToken);

		TestContext.Current.SendDiagnosticMessage(string.Join(" | ", hops.Select(hop => $"{hop.Ttl} {string.Join(',', hop.Addresses)}")));
		Assert.Equal(TraceEnd.Reached, end);
	}

	[Fact]
	public async Task Port_FindsAnHttpsServerOpen()
	{
		SkipUnlessLive();

		PortResult result = await PortCheck.CheckAsync(Cloudflare, 443, TimeSpan.FromSeconds(5), readGreeting: true, TestContext.Current.CancellationToken);

		Assert.Equal(PortState.Open, result.State);
		Assert.Null(result.Greeting);
	}

	private static void SkipUnlessLive() =>
		Assert.SkipUnless(Environment.GetEnvironmentVariable("MOKA_NETWORK_LIVE") == "1", "Set MOKA_NETWORK_LIVE=1 to run against the real network.");
}
