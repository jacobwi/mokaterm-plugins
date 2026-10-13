using System.Text;
using Moka.Network.Targets;
using Moka.Network.Whois;

namespace Moka.Network.Tests.Whois;

public sealed class WhoisTests
{
	private const string Iana = """
		% IANA WHOIS server
		% for more information on IANA, visit http://www.iana.org

		refer:        whois.verisign-grs.com

		domain:       COM
		whois:        whois.verisign-grs.com
		status:       ACTIVE
		""";

	private const string Verisign = """
		   Domain Name: EXAMPLE.COM
		   Registry Domain ID: 2336799_DOMAIN_COM-VRSN
		   Registrar WHOIS Server: whois.markmonitor.com
		   Registrar URL: http://www.markmonitor.com
		""";

	private const string Registrar = """
		Domain Name: example.com
		Registrant Organization: Internet Assigned Numbers Authority
		ReferralServer: whois://whois.example.net
		""";

	[Theory]
	[InlineData(Iana, "whois.verisign-grs.com")]
	[InlineData(Verisign, "whois.markmonitor.com")]
	[InlineData(Registrar, "whois.example.net")]
	[InlineData("whois:        whois.nic.io", "whois.nic.io")]
	[InlineData("ReferralServer:  whois://WHOIS.RIPE.NET/", "whois.ripe.net")]
	[InlineData("refer: whois.example.com:43", "whois.example.com")]
	[InlineData("refer: 127.0.0.1\nrefer: whois.good.example", "whois.good.example")]
	[InlineData("% nothing here\nNetRange: 192.0.2.0 - 192.0.2.255", null)]
	public void Referral_FindsTheNextServer(string answer, string? expected) =>
		Assert.Equal(expected, WhoisReferral.Find(answer.Replace("\r\n", "\n", StringComparison.Ordinal)));

	[Theory]
	[InlineData("rwhois://rwhois.example.net:4321")]
	[InlineData("http://whois.example.com")]
	[InlineData("whois.example.com:8080")]
	[InlineData("127.0.0.1")]
	[InlineData("::1")]
	[InlineData("localhost")]
	[InlineData("whois.localhost")]
	[InlineData("10.1")]
	[InlineData("evil host.com")]
	[InlineData("whois.example.com\u001b[2J")]
	[InlineData("_whois.example.com")]
	[InlineData("*.example.com")]
	[InlineData("")]
	public void Referral_RefusesWhatItWillNotFollow(string value) => Assert.Null(WhoisReferral.ServerFrom(value));

	[Theory]
	[InlineData("example.com", "example.com")]
	[InlineData("EXAMPLE.com.", "example.com")]
	[InlineData("https://www.example.com/path", "www.example.com")]
	[InlineData("bücher.de", "xn--bcher-kva.de")]
	[InlineData("192.0.2.1", "192.0.2.1")]
	[InlineData("[2001:db8::1]", "2001:db8::1")]
	[InlineData("fe80::1%5", "fe80::1")]
	public void Input_ReadsADomainOrAnAddress(string text, string expected)
	{
		Assert.True(WhoisInput.TryParse(text, out string? query, out string? problem), problem);
		Assert.Equal(expected, query);
	}

	[Theory]
	[InlineData("")]
	[InlineData(".")]
	[InlineData("10.1")]
	[InlineData("example.com\r\n-T dn")]
	[InlineData("*.example.com")]
	public void Input_RefusesAnythingElse(string text) => Assert.False(WhoisInput.TryParse(text, out _, out _));

	[Fact]
	public async Task Lookup_FollowsAtMostTwoReferrals()
	{
		FakeTransport transport = new(new()
		{
			["whois.iana.org"] = Iana,
			["whois.verisign-grs.com"] = Verisign,
			["whois.markmonitor.com"] = Registrar,
			["whois.example.net"] = "never asked",
		});
		List<string> shown = [];

		WhoisResult result = await WhoisClient.LookupAsync(transport, "example.com", TimeSpan.FromSeconds(5), answer => shown.Add(answer.Server), TestContext.Current.CancellationToken);

		Assert.Equal(["whois.iana.org", "whois.verisign-grs.com", "whois.markmonitor.com"], transport.Asked);
		Assert.Equal(transport.Asked, shown);
		Assert.Equal(["whois.iana.org", "whois.verisign-grs.com", "whois.markmonitor.com"], result.Answers.Select(answer => answer.Server));
		Assert.Contains("whois.example.net", result.Note, StringComparison.Ordinal);
		Assert.Equal("whois.markmonitor.com", result.Answers[1].Referral);
		Assert.All(transport.Queries, query => Assert.Equal("example.com", query));
	}

	[Fact]
	public async Task Lookup_NeverAsksTheSameServerTwice()
	{
		FakeTransport transport = new(new()
		{
			["whois.iana.org"] = "refer: whois.loop.example",
			["whois.loop.example"] = "refer: whois.iana.org",
		});

		WhoisResult result = await WhoisClient.LookupAsync(transport, "example.com", TimeSpan.FromSeconds(5), _ => { }, TestContext.Current.CancellationToken);

		Assert.Equal(["whois.iana.org", "whois.loop.example"], transport.Asked);
		Assert.Null(result.Note);
	}

	[Fact]
	public async Task Lookup_TakesNoReferralFromAServerNamingItself()
	{
		FakeTransport transport = new(new()
		{
			["whois.iana.org"] = "refer: whois.registrar.example",
			["whois.registrar.example"] = "Registrar WHOIS Server: whois.registrar.example",
		});

		WhoisResult result = await WhoisClient.LookupAsync(transport, "example.com", TimeSpan.FromSeconds(5), _ => { }, TestContext.Current.CancellationToken);

		Assert.Equal("whois.registrar.example", result.Answers[0].Referral);
		Assert.Null(result.Answers[1].Referral);
		Assert.Null(result.Note);
	}

	[Fact]
	public async Task Lookup_KeepsTheAnswersSoFarWhenAReferralFails()
	{
		FakeTransport transport = new(new() { ["whois.iana.org"] = Iana });

		WhoisResult result = await WhoisClient.LookupAsync(transport, "example.com", TimeSpan.FromSeconds(5), _ => { }, TestContext.Current.CancellationToken);

		Assert.Equal(2, result.Answers.Count);
		Assert.Null(result.Answers[0].Error);
		Assert.Equal("whois.verisign-grs.com", result.Answers[1].Server);
		Assert.Contains("no such server", result.Answers[1].Error, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Lookup_ThrowsWhenTheFirstServerFails()
	{
		FakeTransport transport = new(new());

		await Assert.ThrowsAsync<ProbeException>(() => WhoisClient.LookupAsync(transport, "example.com", TimeSpan.FromSeconds(5), _ => { }, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Lookup_HoldsEveryAnswerToOneSizeLimit()
	{
		string big = "refer: whois.next.example\n" + new string('x', WhoisClient.MaxTotalBytes);
		FakeTransport transport = new(new() { ["whois.iana.org"] = big, ["whois.next.example"] = "never asked" });

		WhoisResult result = await WhoisClient.LookupAsync(transport, "example.com", TimeSpan.FromSeconds(5), _ => { }, TestContext.Current.CancellationToken);

		WhoisAnswer answer = Assert.Single(result.Answers);
		Assert.True(answer.Cut);
		Assert.Equal(WhoisClient.MaxTotalBytes, answer.Bytes);
		Assert.Equal([WhoisClient.MaxTotalBytes], transport.Limits);
		Assert.Contains("limit", result.Note, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Lookup_PassesWhatIsLeftOfTheLimitToTheNextServer()
	{
		FakeTransport transport = new(new() { ["whois.iana.org"] = "refer: whois.next.example\n", ["whois.next.example"] = "done" });

		await WhoisClient.LookupAsync(transport, "example.com", TimeSpan.FromSeconds(5), _ => { }, TestContext.Current.CancellationToken);

		Assert.Equal([WhoisClient.MaxTotalBytes, WhoisClient.MaxTotalBytes - 26], transport.Limits);
	}

	[Fact]
	public async Task Lookup_ShowsAnswersAsPlainText()
	{
		FakeTransport transport = new(new() { ["whois.iana.org"] = "Name: \u001b[31mEVIL\u001b[0m‮txt.exe\r\nStatus: ok\u0007" });

		WhoisResult result = await WhoisClient.LookupAsync(transport, "example.com", TimeSpan.FromSeconds(5), _ => { }, TestContext.Current.CancellationToken);

		Assert.Equal("Name: EVILtxt.exe\nStatus: ok", Assert.Single(result.Answers).Text);
	}

	[Fact]
	public void PlainText_ReadsLatin1WhenTheBytesAreNotUtf8()
	{
		Assert.Equal("Müller", PlainText.FromBytes([(byte)'M', 0xFC, (byte)'l', (byte)'l', (byte)'e', (byte)'r']));
		Assert.Equal("Müller", PlainText.FromBytes(Encoding.UTF8.GetBytes("Müller")));
	}

	/// <summary>Answers from memory, cut to the limit the client passes, the way the TCP transport cuts a real answer.</summary>
	private sealed class FakeTransport(Dictionary<string, string> answers) : IWhoisTransport
	{
		public List<string> Asked { get; } = [];

		public List<string> Queries { get; } = [];

		public List<int> Limits { get; } = [];

		public Task<WhoisRaw> AskAsync(string server, string query, int maxBytes, TimeSpan timeout, CancellationToken cancellationToken)
		{
			Asked.Add(server);
			Queries.Add(query);
			Limits.Add(maxBytes);
			if (!answers.TryGetValue(server, out string? answer))
			{
				throw new ProbeException($"{server}: no such server.");
			}

			byte[] bytes = Encoding.UTF8.GetBytes(answer);
			bool cut = bytes.Length > maxBytes;
			return Task.FromResult(new WhoisRaw(cut ? bytes[..maxBytes] : bytes, cut, false));
		}
	}
}
