using Moka.Network.Server;

namespace Moka.Network.Tests.Server;

public sealed class ServerTraceOutputTests
{
	[Fact]
	public void Parse_ReadsTracepathMergingAHopsLinesAndSkippingTheLocalLine()
	{
		const string text = """
			 1?: [LOCALHOST]                      pmtu 1500
			 1:  10.0.0.1                                              0.345ms
			 1:  10.0.0.1                                              0.289ms
			 2:  no reply
			 3:  93.184.216.34                                        11.123ms reached
			     Resume: pmtu 1500 hops 3 back 3
			""";

		IReadOnlyList<ServerTraceHop> hops = ServerTraceOutput.Parse(text);

		Assert.Equal(
			[
				new ServerTraceHop(1, "10.0.0.1", 0.289),
				new ServerTraceHop(2, null, null),
				new ServerTraceHop(3, "93.184.216.34", 11.123),
			],
			hops);
	}

	[Fact]
	public void Parse_ReadsTracerouteWithItsHeaderAndSilentHops()
	{
		const string text = """
			traceroute to example.com (93.184.216.34), 20 hops max, 60 byte packets
			 1  10.0.0.1  0.412 ms
			 2  *
			 3  198.51.100.7  4.870 ms  4.512 ms
			12  93.184.216.34  11.020 ms
			""";

		IReadOnlyList<ServerTraceHop> hops = ServerTraceOutput.Parse(text);

		Assert.Equal([1, 2, 3, 12], hops.Select(hop => hop.Hop));
		Assert.Null(hops[1].Milliseconds);
		Assert.Equal(4.512, hops[2].Milliseconds);
		Assert.Equal("93.184.216.34", hops[3].Address);
	}

	[Fact]
	public void Parse_TextWithNoHops_GivesNone()
	{
		Assert.Empty(ServerTraceOutput.Parse("tracepath: example.invalid: Name or service not known\n"));
	}
}
