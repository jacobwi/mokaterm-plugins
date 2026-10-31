using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using Moka.Network.Subnets;

namespace Moka.Network.Lookup.Mapping;

/// <summary>One address of the sweep and the name its PTR record gave, or null when nothing answered for it.</summary>
public sealed record PtrSweepItem(IPAddress Address, string? Name)
{
	/// <summary>No PTR record came back: the address has no name anyone wrote down.</summary>
	public bool Silent => Name is null;
}

/// <summary>Every address of a prefix with its name or its silence, in address order.</summary>
public sealed record PtrSweepResult
{
	public required IReadOnlyList<PtrSweepItem> Items { get; init; }

	public int Found => Items.Count(item => !item.Silent);

	public int Silent => Items.Count(item => item.Silent);
}

/// <summary>
/// Asks the PTR record of every address in an IPv4 prefix, a handful at a time, and lists the names that come back
/// against the silence of the rest. Pure logic driven by a lookup function, the way
/// <see cref="DnsLookup.ReverseAsync"/> is meant to be reused.
/// </summary>
public static class PtrSweep
{
	/// <summary>The widest prefix one sweep asks: a /24, which is 256 questions already.</summary>
	public const int WidestPrefixLength = 24;

	/// <summary>The most addresses that is: the size of a /24.</summary>
	public const int MaxAddresses = 1 << (32 - WidestPrefixLength);

	/// <summary>How many PTR questions are in flight at once.</summary>
	public const int MaxAtOnce = 8;

	/// <summary>
	/// The addresses of <paramref name="subnet"/> in order, or the reason a sweep of it cannot be run: an IPv6 prefix,
	/// or one wider than /24, with the count so the problem can say how big it is.
	/// </summary>
	public static bool TryAddresses(SubnetInfo subnet, [NotNullWhen(true)] out IReadOnlyList<IPAddress>? addresses, [NotNullWhen(false)] out string? problem)
	{
		ArgumentNullException.ThrowIfNull(subnet);
		if (subnet.IsIPv6)
		{
			addresses = null;
			problem = $"{subnet.Cidr} is an IPv6 prefix; a PTR sweep asks one name per address, and even the smallest useful IPv6 prefix has far too many to ask.";
			return false;
		}

		if (subnet.PrefixLength < WidestPrefixLength)
		{
			addresses = null;
			problem = $"{subnet.Cidr} holds {subnet.AddressCount.ToString("N0", CultureInfo.InvariantCulture)} addresses, and a PTR sweep asks every one. Prefixes up to /{WidestPrefixLength} can be swept ({MaxAddresses} addresses at most).";
			return false;
		}

		UInt128 network = SubnetMath.ToNumber(subnet.Network);
		addresses = [.. Enumerable.Range(0, (int)subnet.AddressCount).Select(offset => SubnetMath.ToAddress(network + (UInt128)offset, v4: true))];
		problem = null;
		return true;
	}

	/// <summary>
	/// Asks every address in <paramref name="addresses"/>, <see cref="MaxAtOnce"/> at a time, reporting each answer as
	/// it lands so a progress line can keep up. The items come back in address order whatever order the answers arrive in.
	/// </summary>
	public static async Task<PtrSweepResult> RunAsync(Func<IPAddress, CancellationToken, Task<string?>> lookup, IReadOnlyList<IPAddress> addresses, Action<int> progress, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(lookup);
		ArgumentNullException.ThrowIfNull(addresses);
		ArgumentNullException.ThrowIfNull(progress);
		PtrSweepItem[] items = new PtrSweepItem[addresses.Count];
		using SemaphoreSlim gate = new(MaxAtOnce, MaxAtOnce);
		int done = 0;
		await Task.WhenAll(addresses.Select(async (address, index) =>
		{
			await gate.WaitAsync(cancellationToken);
			try
			{
				items[index] = new PtrSweepItem(address, await lookup(address, cancellationToken));
			}
			finally
			{
				gate.Release();
				progress(Interlocked.Increment(ref done));
			}
		}));
		return new PtrSweepResult { Items = [.. items] };
	}
}
