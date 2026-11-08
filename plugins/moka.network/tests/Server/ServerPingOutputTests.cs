using Moka.Network.Server;

namespace Moka.Network.Tests.Server;

public sealed class ServerPingOutputTests
{
	[Fact]
	public void Parse_ReadsIputilsRepliesAndTheSummary()
	{
		const string text = """
			PING example.com (93.184.216.34) 56(84) bytes of data.
			64 bytes from 93.184.216.34: icmp_seq=1 ttl=56 time=11.6 ms
			64 bytes from 93.184.216.34: icmp_seq=3 ttl=56 time=12.45 ms

			--- example.com ping statistics ---
			3 packets transmitted, 2 received, 33.3333% packet loss, time 2003ms
			rtt min/avg/max/mdev = 11.600/12.025/12.450/0.425 ms
			""";

		ServerPingResult result = ServerPingOutput.Parse(text);

		Assert.Equal([new ServerPingReply(1, 11.6), new ServerPingReply(3, 12.45)], result.Replies);
		Assert.Equal(3, result.Sent);
		Assert.Equal(2, result.Answered);
		Assert.Equal(100.0 / 3, result.LossPercent, 3);
		Assert.Equal(11.6, result.Minimum);
		Assert.Equal(12.45, result.Maximum);
	}

	[Fact]
	public void Parse_ReadsBusyBoxRepliesAndItsSummaryWording()
	{
		const string text = """
			PING 1.1.1.1 (1.1.1.1): 56 data bytes
			64 bytes from 1.1.1.1: seq=0 ttl=57 time=9.921 ms
			64 bytes from 1.1.1.1: seq=1 ttl=57 time=10.112 ms

			--- 1.1.1.1 ping statistics ---
			2 packets transmitted, 2 packets received, 0% packet loss
			""";

		ServerPingResult result = ServerPingOutput.Parse(text);

		Assert.Equal(2, result.Replies.Count);
		Assert.Equal(0, result.Replies[0].Sequence);
		Assert.Equal(2, result.Answered);
		Assert.Equal(0, result.LossPercent);
	}

	[Fact]
	public void Parse_WithoutASummary_CountsTheRepliesItSaw()
	{
		ServerPingResult result = ServerPingOutput.Parse("64 bytes from 10.0.0.1: icmp_seq=1 ttl=64 time=0.045 ms\nping: sendmsg: Network is unreachable\n");

		Assert.Single(result.Replies);
		Assert.Equal(1, result.Sent);
		Assert.Null(result.Transmitted);
	}

	[Fact]
	public void Parse_ErrorsAlone_GiveNothingToChart()
	{
		ServerPingResult result = ServerPingOutput.Parse("ping: unknown host nowhere.invalid\n");

		Assert.Empty(result.Replies);
		Assert.Equal(0, result.Sent);
		Assert.Null(result.Average);
	}
}
