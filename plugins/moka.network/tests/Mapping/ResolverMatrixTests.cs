using Moka.Network.Lookup;
using Moka.Network.Lookup.Mapping;

namespace Moka.Network.Tests.Mapping;

public sealed class ResolverMatrixTests
{
	private static readonly IReadOnlyList<DnsResolverKind> PublicKinds =
		[DnsResolverKind.Cloudflare, DnsResolverKind.Google, DnsResolverKind.Quad9];

	private const int A = (int)DnsRecordType.A;

	[Fact]
	public async Task RunAsync_LinesUpOneRowPerResolver()
	{
		ResolverMatrix.Query query = (_, _, _, _) => Task.FromResult(Replies.LookupResult(
			Replies.Message(answers:
			[
				Replies.Record("example.com", DnsRecordType.A, "93.184.216.34"),
				Replies.Record("example.com", DnsRecordType.A, "93.184.216.35"),
			]),
			Replies.Root));

		ResolverMatrixResult matrix = await ResolverMatrix.RunAsync(PublicKinds, custom: null, query, "example.com", A, CancellationToken.None);

		Assert.Equal(3, matrix.Rows.Count);
		Assert.All(matrix.Rows, row => Assert.True(row.Answered));
		Assert.Equal(["Cloudflare 1.1.1.1", "Google 8.8.8.8", "Quad9 9.9.9.9"], matrix.Rows.Select(row => row.Resolver));
		Assert.All(matrix.Rows, row => Assert.Equal("NOERROR", row.Code));
		Assert.All(matrix.Rows, row => Assert.Equal("A 93.184.216.34 (+1 more)", row.Answer));
		Assert.Empty(matrix.Disagreements);
	}

	[Fact]
	public async Task RunAsync_CountsReorderedAnswersAsAgreement()
	{
		int asked = 0;
		ResolverMatrix.Query query = (_, _, _, _) => Task.FromResult(Replies.LookupResult(Replies.Message(answers: asked++ % 2 == 0
			? [Replies.Record("example.com", DnsRecordType.A, "93.184.216.34"), Replies.Record("example.com", DnsRecordType.A, "93.184.216.35")]
			: [Replies.Record("example.com", DnsRecordType.A, "93.184.216.35"), Replies.Record("example.com", DnsRecordType.A, "93.184.216.34")])));

		ResolverMatrixResult matrix = await ResolverMatrix.RunAsync(PublicKinds, custom: null, query, "example.com", A, CancellationToken.None);

		Assert.Empty(matrix.Disagreements);
	}

	[Fact]
	public async Task RunAsync_CallsOutWhereTheResolversDisagree()
	{
		ResolverMatrix.Query query = (servers, _, _, _) => Task.FromResult(servers[0].Address.ToString() == "1.1.1.1"
			? Replies.LookupResult(Replies.Message(answers: [Replies.Record("example.com", DnsRecordType.A, "93.184.216.34")]))
			: Replies.LookupResult(Replies.Message(rcode: 3), Replies.Tld));

		ResolverMatrixResult matrix = await ResolverMatrix.RunAsync(PublicKinds, custom: null, query, "example.com", A, CancellationToken.None);

		Assert.NotEmpty(matrix.Disagreements);
		Assert.Contains(matrix.Disagreements, note => note.Contains("Cloudflare 1.1.1.1 says", StringComparison.Ordinal));
		Assert.Contains(matrix.Disagreements, note => note.Contains("Google 8.8.8.8, Quad9 9.9.9.9 say", StringComparison.Ordinal));
		Assert.Contains(matrix.Disagreements, note => note.Contains("do not agree", StringComparison.Ordinal));
	}

	[Fact]
	public async Task RunAsync_WordsAResolverThatCannotBeReached()
	{
		ResolverMatrix.Query query = (servers, _, _, _) => servers[0].Address.ToString() == "8.8.8.8"
			? throw new DnsLookupException("8.8.8.8:53 did not answer within 3 s.")
			: Task.FromResult(Replies.LookupResult(Replies.Message(answers: [Replies.Record("example.com", DnsRecordType.A, "93.184.216.34")])));

		ResolverMatrixResult matrix = await ResolverMatrix.RunAsync(PublicKinds, custom: null, query, "example.com", A, CancellationToken.None);

		ResolverMatrixRow failed = Assert.Single(matrix.Rows, row => !row.Answered);
		Assert.Equal("Google 8.8.8.8", failed.Resolver);
		Assert.Contains("did not answer within 3 s", failed.Failure, StringComparison.Ordinal);
		Assert.Null(failed.Code);
	}

	[Fact]
	public async Task RunAsync_AsksTheCustomResolverWhenItsAddressIsSet()
	{
		ResolverMatrix.Query query = (servers, _, _, _) => Task.FromResult(Replies.LookupResult(
			Replies.Message(answers: [Replies.Record("example.com", DnsRecordType.A, "192.168.53.1")]),
			servers[0]));

		ResolverMatrixResult matrix = await ResolverMatrix.RunAsync(
			[.. PublicKinds, DnsResolverKind.Custom], "192.168.53.1", query, "example.com", A, CancellationToken.None);

		ResolverMatrixRow custom = Assert.Single(matrix.Rows, row => row.Resolver == "Custom");
		Assert.True(custom.Answered);
		Assert.Equal("A 192.168.53.1", custom.Answer);
	}

	[Fact]
	public async Task RunAsync_WordsACustomResolverWithoutAnAddress()
	{
		ResolverMatrix.Query query = (_, _, _, _) => Task.FromResult(Replies.LookupResult(Replies.Message()));

		ResolverMatrixResult matrix = await ResolverMatrix.RunAsync(
			[DnsResolverKind.Custom], "not an address", query, "example.com", A, CancellationToken.None);

		ResolverMatrixRow row = Assert.Single(matrix.Rows);
		Assert.False(row.Answered);
		Assert.Contains("is not an IP address", row.Failure, StringComparison.Ordinal);
		Assert.Empty(matrix.Disagreements);
	}

	[Fact]
	public async Task RunAsync_ShowsTheRcodeOfAnAnswerThatIsNotAnErrorToTheTool()
	{
		ResolverMatrix.Query query = (_, _, _, _) => Task.FromResult(Replies.LookupResult(Replies.Message(rcode: 5), Replies.Root));

		ResolverMatrixResult matrix = await ResolverMatrix.RunAsync([DnsResolverKind.Cloudflare], custom: null, query, "example.com", A, CancellationToken.None);

		ResolverMatrixRow row = Assert.Single(matrix.Rows);
		Assert.True(row.Answered);
		Assert.Equal("REFUSED", row.Code);
		Assert.Null(row.Answer);
	}
}
