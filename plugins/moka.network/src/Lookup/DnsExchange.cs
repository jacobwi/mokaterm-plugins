using System.Net;

namespace Moka.Network.Lookup;

/// <summary>One question answered by one server: the reply, who sent it, how long it took and how it travelled.</summary>
public sealed record DnsExchange
{
	public required DnsMessage Message { get; init; }

	public required IPEndPoint Server { get; init; }

	public required TimeSpan Elapsed { get; init; }

	/// <summary>The reply came over TCP, because the UDP one was cut.</summary>
	public bool OverTcp { get; init; }

	/// <summary>Datagrams that arrived with another id or another question and were passed over.</summary>
	public int IgnoredReplies { get; init; }
}
