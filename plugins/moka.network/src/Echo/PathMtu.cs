namespace Moka.Network.Echo;

/// <summary>
/// The largest payload that fits the whole way to one host, found by binary search over DF-set IPv4 echo requests:
/// each probe halves the gap between a size known to pass and one known to fail, so about eleven probes pin down the
/// boundary. Which link is too small stays hidden: the answer that reports a too-big packet names no router.
/// </summary>
public static class PathMtu
{
	/// <summary>The smallest payload a probe can carry: an empty one.</summary>
	public const int MinPayload = 0;

	/// <summary>The largest payload searched: what fits the classic 1500 byte link, leaving 20 bytes of IP header and 8 of ICMP.</summary>
	public const int MaxPayload = 1472;

	/// <summary>What the IP and ICMP headers add to the payload on the wire.</summary>
	public const int HeaderBytes = 28;

	/// <summary>
	/// Finds the largest payload size that passes, asking <paramref name="fits"/> about one size at a time. The search
	/// covers <see cref="MinPayload"/> to <see cref="MaxPayload"/> and stops after about eleven probes.
	/// </summary>
	public static async Task<PathMtuResult> FindAsync(Func<int, CancellationToken, Task<bool>> fits, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(fits);
		int low = MinPayload - 1;
		int high = MaxPayload + 1;
		int probes = 0;
		while (high - low > 1)
		{
			int middle = (low + high) / 2;
			if (await fits(middle, cancellationToken))
			{
				low = middle;
			}
			else
			{
				high = middle;
			}

			probes++;
		}

		return new PathMtuResult(low >= MinPayload ? low : null, probes);
	}
}

/// <summary>What the search found: the largest passing payload, or null when not even an empty one got through.</summary>
public sealed record PathMtuResult(int? LargestPayload, int Probes)
{
	/// <summary>The payload plus the 28 bytes of header around it: the largest IPv4 packet the path carries whole. Null when nothing passed.</summary>
	public int? PathMtuBytes => LargestPayload is { } payload ? payload + PathMtu.HeaderBytes : null;

	/// <summary>Every size up to the search's ceiling passed, so the path may carry more than the search can see.</summary>
	public bool AtCeiling => LargestPayload == PathMtu.MaxPayload;
}
