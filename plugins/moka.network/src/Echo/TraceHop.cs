using System.Net;

namespace Moka.Network.Echo;

/// <summary>
/// One hop of a trace as it stands: its TTL and the probes sent so far. A hop can answer from more than one address
/// when the path is balanced over several routers, so <see cref="Addresses"/> lists each one once, in arrival order.
/// </summary>
public sealed record TraceHop(int Ttl, IReadOnlyList<TraceProbe> Probes)
{
	public IReadOnlyList<IPAddress> Addresses => [.. Probes.Select(probe => probe.From).OfType<IPAddress>().Distinct()];

	/// <summary>The destination itself answered one of this hop's probes.</summary>
	public bool Reached => Probes.Any(probe => probe.Status == EchoStatus.Reply);

	/// <summary>A probe was answered, but the answer did not say by whom.</summary>
	public bool AddressMissing => Probes.Any(probe => probe.AddressMissing);

	/// <summary>Every probe sent so far went unanswered.</summary>
	public bool Silent => Probes.Count > 0 && Probes.All(probe => probe.Status == EchoStatus.TimedOut);

	public TraceProbe? Unreachable => Probes.FirstOrDefault(probe => probe.Status == EchoStatus.Unreachable);
}
