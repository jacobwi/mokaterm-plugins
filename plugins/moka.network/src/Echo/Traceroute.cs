using System.Net;

namespace Moka.Network.Echo;

/// <summary>
/// Traceroute over ICMP echo: TTL 1, 2, 3 and on, a few probes each, one at a time so the routers see the same pace a
/// classic traceroute keeps. Every probe's outcome is handed over as the hop stands at that moment, so a view can draw
/// the hop while its later probes are still out. Ends after the hop where the destination answered or said it cannot
/// be reached, or after the last TTL; cancelling ends it where it is.
/// </summary>
public static class Traceroute
{
	public static async Task<TraceEnd> RunAsync(IPinger pinger, IPAddress destination, TraceOptions options, Action<TraceHop> onProbe, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(pinger);
		ArgumentNullException.ThrowIfNull(destination);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(onProbe);
		int maxHops = Math.Clamp(options.MaxHops, 1, TraceOptions.MaxMaxHops);
		int probes = Math.Clamp(options.ProbesPerHop, 1, 10);
		for (int ttl = 1; ttl <= maxHops; ttl++)
		{
			EchoRequest request = new(ttl, options.Timeout, options.PayloadSize);
			List<TraceProbe> sent = [];
			for (int index = 0; index < probes; index++)
			{
				EchoReply reply = await pinger.SendAsync(destination, request, cancellationToken);
				sent.Add(new TraceProbe(reply.Status, reply.From, reply.Status is EchoStatus.Reply or EchoStatus.TtlExpired ? reply.Milliseconds : null, reply.Detail));
				onProbe(new TraceHop(ttl, [.. sent]));
			}

			TraceHop hop = new(ttl, sent);
			if (hop.Reached)
			{
				return TraceEnd.Reached;
			}

			if (hop.Unreachable is not null)
			{
				return TraceEnd.Unreachable;
			}
		}

		return TraceEnd.OutOfHops;
	}
}
