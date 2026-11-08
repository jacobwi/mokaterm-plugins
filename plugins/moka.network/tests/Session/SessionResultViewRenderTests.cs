using System.Net;
using System.Text;
using Moka.Network.Server;
using Moka.Network.Session;
using Mokaterm.Abstractions.Commands;
using Mokaterm.Plugin.TestHost;

namespace Moka.Network.Tests.Session;

/// <summary>The session tool's result views with answers written here, as a server would have printed them.</summary>
public sealed class SessionResultViewRenderTests
{
	[Fact]
	public async Task Getent_ListsEveryAddressWithItsFamilyAndNames()
	{
		GetentHost[] rows =
		[
			new(IPAddress.Parse("93.184.216.34"), "example.com", []),
			new(IPAddress.Parse("2606:2800:220:1::248"), "example.com", ["www.example.com"]),
		];

		string html = await StaticRender.RenderAsync<GetentAnswerView>(new()
		{
			[nameof(GetentAnswerView.Target)] = "example.com on the server",
			[nameof(GetentAnswerView.Rows)] = rows,
		});

		Assert.Contains("example.com on the server", html, StringComparison.Ordinal);
		Assert.Contains("93.184.216.34", html, StringComparison.Ordinal);
		Assert.Contains("IPv6", html, StringComparison.Ordinal);
		Assert.Contains("example.com www.example.com", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ListeningPorts_CountsTheSocketsAndNamesTheirProcesses()
	{
		SsRow[] rows =
		[
			new("tcp", "LISTEN", "0.0.0.0:22", "sshd", "users:((\"sshd\",pid=812,fd=3))"),
			new("udp", "UNCONN", "127.0.0.53%lo:53", "", ""),
		];

		string html = await StaticRender.RenderAsync<ListeningPortsView>(new() { [nameof(ListeningPortsView.Rows)] = rows });

		Assert.Contains("Listening sockets", html, StringComparison.Ordinal);
		Assert.Contains("0.0.0.0:22", html, StringComparison.Ordinal);
		Assert.Contains("sshd", html, StringComparison.Ordinal);
		Assert.Contains("127.0.0.53%lo:53", html, StringComparison.Ordinal);
		Assert.DoesNotContain("root sees every one", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ListeningPorts_WithNoProcessNamed_SaysWhoCanSeeThem()
	{
		string html = await StaticRender.RenderAsync<ListeningPortsView>(new()
		{
			[nameof(ListeningPortsView.Rows)] = new[] { new SsRow("tcp", "LISTEN", "0.0.0.0:22", "", "") },
		});

		Assert.Contains("root sees every one", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Reach_ShowsTheVerdictThePingFiguresAndTheTraceBars()
	{
		const string ping = """
			PING example.com (93.184.216.34) 56(84) bytes of data.
			64 bytes from 93.184.216.34: icmp_seq=1 ttl=56 time=11.6 ms
			64 bytes from 93.184.216.34: icmp_seq=2 ttl=56 time=12.4 ms

			--- example.com ping statistics ---
			3 packets transmitted, 2 received, 33.3333% packet loss, time 2003ms
			""";
		const string trace = """
			 1?: [LOCALHOST]                      pmtu 1500
			 1:  10.0.0.1                                              0.345ms
			 2:  no reply
			 3:  93.184.216.34                                        11.123ms reached
			     Resume: pmtu 1500 hops 3 back 3
			""";

		string html = await StaticRender.RenderAsync<ReachResultView>(new()
		{
			[nameof(ReachResultView.TcpTarget)] = "example.com:443 from the server",
			[nameof(ReachResultView.Tcp)] = new ReachVerdict(ReachState.Open, "The server opened a TCP connection."),
			[nameof(ReachResultView.PingTarget)] = "example.com (3 requests)",
			[nameof(ReachResultView.PingText)] = ping,
			[nameof(ReachResultView.TraceTarget)] = "example.com",
			[nameof(ReachResultView.TraceText)] = trace,
			[nameof(ReachResultView.Tracer)] = "tracepath",
		});

		Assert.Contains("example.com:443 from the server: open", html, StringComparison.Ordinal);
		Assert.Contains("2 of 3", html, StringComparison.Ordinal);
		Assert.Contains("33.3%", html, StringComparison.Ordinal);
		Assert.Contains("moka-linechart", html, StringComparison.Ordinal);
		Assert.Contains("Trace from the server (tracepath)", html, StringComparison.Ordinal);
		Assert.Contains("moka-barchart", html, StringComparison.Ordinal);
		Assert.Contains("icmp_seq=2", html, StringComparison.Ordinal);
		Assert.Contains("Copy the trace output", html, StringComparison.Ordinal);
	}

	[Fact]
	public async Task Capture_ListsThePacketsNewestFirstWithTheCounts()
	{
		CaptureLines lines = new();
		byte[] text = Encoding.UTF8.GetBytes(
			"12:00:01.000001 IP 10.0.0.2.51514 > 10.0.0.1.22: Flags [P.], length 36\n" +
			"12:00:02.000002 IP 10.0.0.1.22 > 10.0.0.2.51514: Flags [.], length 0\n");
		lines.Write(CommandStream.Output, text);
		IReadOnlyList<CaptureEntry> rows = lines.Snapshot();

		string html = await StaticRender.RenderAsync<CaptureView>(new()
		{
			[nameof(CaptureView.Rows)] = rows,
			[nameof(CaptureView.Total)] = lines.Total,
			[nameof(CaptureView.Filter)] = "tcp port 22",
			[nameof(CaptureView.AllText)] = lines.AllText(),
		});

		Assert.Contains("aria-label=\"Captured packets\"", html, StringComparison.Ordinal);
		Assert.Contains("filter tcp port 22", html, StringComparison.Ordinal);
		Assert.Contains("Copy capture", html, StringComparison.Ordinal);
		Assert.True(
			html.IndexOf("12:00:02.000002", StringComparison.Ordinal) < html.IndexOf("12:00:01.000001", StringComparison.Ordinal),
			"The newest packet comes first.");
	}
}
