using Moka.Network.Server;

namespace Moka.Network.Tests.Server;

public sealed class SsListingTests
{
	private const string Listing = """
		Netid  State  Recv-Q Send-Q Local Address:Port   Peer Address:Port  Process
		udp    UNCONN 0      0      0.0.0.0:68           0.0.0.0:*           users:(("NetworkManager",pid=777,fd=20))
		tcp    LISTEN 0      128    127.0.0.1:631        0.0.0.0:*
		tcp    LISTEN 0      511    *:80                 *:*                 users:(("nginx",pid=900,fd=6))
		tcp    LISTEN 0      511    [::]:443             [::]:*              users:(("nginx",pid=900,fd=7),("nginx",pid=901,fd=7))
		udp    UNCONN 0      0      [fe80::1%eth0]:546   [::]:*              users:(("my daemon",pid=4300,fd=20))
		tcp    ESTAB  0      0      10.0.0.5%eth0:22     203.0.113.7:52931   users:(("sshd",pid=1234,fd=4))
		""";

	[Fact]
	public void Parse_LeavesTheHeaderBehind()
	{
		IReadOnlyList<SsRow> rows = SsListing.Parse(Listing);

		Assert.Equal(6, rows.Count);
		Assert.DoesNotContain(rows, row => row.Netid == "Netid");
	}

	[Fact]
	public void Parse_ReadsTheColumnsOfAListeningSocket()
	{
		SsRow row = SsListing.Parse(Listing)[1];

		Assert.Equal("tcp", row.Netid);
		Assert.Equal("LISTEN", row.State);
		Assert.Equal("127.0.0.1:631", row.Local);
		Assert.Equal("", row.Process);
		Assert.Equal("", row.ProcessDetail);
	}

	[Fact]
	public void Parse_KeepsTheAddressAsSsPrintedIt_WildcardsAndBracketsAndScope()
	{
		IReadOnlyList<SsRow> rows = SsListing.Parse(Listing);

		Assert.Equal("*:80", rows[2].Local);
		Assert.Equal("[::]:443", rows[3].Local);
		Assert.Equal("[fe80::1%eth0]:546", rows[4].Local);
		Assert.Equal("10.0.0.5%eth0:22", rows[5].Local);
	}

	[Fact]
	public void Parse_NamesEachProcessOnce_EvenWhenTwoPidsHoldTheSocket()
	{
		IReadOnlyList<SsRow> rows = SsListing.Parse(Listing);

		Assert.Equal("nginx", rows[2].Process);
		Assert.Equal("nginx", rows[3].Process);
		Assert.StartsWith("users:((\"nginx\",pid=900,fd=7)", rows[3].ProcessDetail, StringComparison.Ordinal);
	}

	[Fact]
	public void Parse_ReadsAProcessNameWithASpaceInIt()
	{
		SsRow row = SsListing.Parse(Listing)[4];

		Assert.Equal("my daemon", row.Process);
	}

	[Fact]
	public void Parse_SkipsALineWithoutTheSixColumnsSsPromises()
	{
		string text = "tcp LISTEN 0 128 broken-line\n\nudp UNCONN 0 0 0.0.0.0:68 0.0.0.0:*\n";

		IReadOnlyList<SsRow> rows = SsListing.Parse(text);

		SsRow row = Assert.Single(rows);
		Assert.Equal("0.0.0.0:68", row.Local);
	}

	[Fact]
	public void Parse_EmptyIsAnEmptyListing()
	{
		Assert.Empty(SsListing.Parse(""));
	}
}
