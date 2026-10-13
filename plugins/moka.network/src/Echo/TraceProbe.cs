using System.Net;

namespace Moka.Network.Echo;

/// <summary>One of a hop's probes: what came back, from whom, and how long it took.</summary>
public sealed record TraceProbe(EchoStatus Status, IPAddress? From, double? Milliseconds, string? Detail = null)
{
	/// <summary>A router said its TTL expired, or the destination answered, without saying which machine it is.</summary>
	public bool AddressMissing => From is null && Status is EchoStatus.TtlExpired or EchoStatus.Reply;
}
