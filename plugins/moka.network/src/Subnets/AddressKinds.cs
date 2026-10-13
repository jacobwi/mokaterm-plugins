using System.Net;
using System.Net.Sockets;

namespace Moka.Network.Subnets;

/// <summary>
/// Sorts an address into its kind from the IANA IPv4 and IPv6 special-purpose address registries, the most specific
/// range first, so 192.0.2.1 is documentation rather than the wider 192.0.0.0/24 and 255.255.255.255 is the limited
/// broadcast rather than reserved space. Whatever no range claims is public.
/// </summary>
public static class AddressKinds
{
	private static readonly Entry[] V4 = Sorted(
	[
		new("0.0.0.0/8", "This network", false, "Only valid as a source while a host learns its own address."),
		new("10.0.0.0/8", "Private", false, "RFC 1918: routed inside an organization, never on the internet."),
		new("100.64.0.0/10", "Shared (CGNAT)", false, "RFC 6598: between a provider's NAT and its customers."),
		new("127.0.0.0/8", "Loopback", false, "This machine itself."),
		new("169.254.0.0/16", "Link-local", false, "Self-assigned when no DHCP answered; not routed."),
		new("172.16.0.0/12", "Private", false, "RFC 1918: routed inside an organization, never on the internet."),
		new("192.0.0.0/24", "IETF protocol assignments", false),
		new("192.0.2.0/24", "Documentation", false, "TEST-NET-1, for examples; never in use."),
		new("192.88.99.0/24", "6to4 relay (deprecated)", false),
		new("192.168.0.0/16", "Private", false, "RFC 1918: routed inside an organization, never on the internet."),
		new("198.18.0.0/15", "Benchmarking", false, "RFC 2544: for testing network equipment."),
		new("198.51.100.0/24", "Documentation", false, "TEST-NET-2, for examples; never in use."),
		new("203.0.113.0/24", "Documentation", false, "TEST-NET-3, for examples; never in use."),
		new("224.0.0.0/4", "Multicast", false, "Names a group of receivers, not one machine."),
		new("240.0.0.0/4", "Reserved", false, "Set aside for future use; not routed."),
		new("255.255.255.255/32", "Limited broadcast", false, "Every machine on this link."),
	]);

	private static readonly Entry[] V6 = Sorted(
	[
		new("::/128", "Unspecified", false),
		new("::1/128", "Loopback", false, "This machine itself."),
		new("::ffff:0:0/96", "IPv4-mapped", false, "An IPv4 address written as IPv6."),
		new("64:ff9b::/96", "NAT64", false, "IPv4 reached through a translator."),
		new("64:ff9b:1::/48", "Local NAT64", false),
		new("100::/64", "Discard-only", false),
		new("2001::/32", "Teredo", true, "IPv6 tunnelled over IPv4 UDP."),
		new("2001:2::/48", "Benchmarking", false),
		new("2001:db8::/32", "Documentation", false, "For examples; never in use."),
		new("2001:20::/28", "ORCHIDv2", false),
		new("2002::/16", "6to4", true, "IPv6 tunnelled over IPv4."),
		new("3fff::/20", "Documentation", false, "RFC 9637, for examples; never in use."),
		new("fc00::/7", "Unique local", false, "IPv6's private range: routed inside an organization, never on the internet."),
		new("fe80::/10", "Link-local", false, "Every interface has one; not routed, and reached with a zone such as %eth0."),
		new("fec0::/10", "Site-local (deprecated)", false),
		new("ff00::/8", "Multicast", false, "Names a group of receivers, not one machine."),
		new("2000::/3", "Global unicast", true),
	]);

	public static AddressKind Classify(IPAddress address)
	{
		ArgumentNullException.ThrowIfNull(address);
		bool v6 = address.AddressFamily == AddressFamily.InterNetworkV6;
		foreach (Entry entry in v6 ? V6 : V4)
		{
			if (SubnetMath.Contains(entry.Subnet, address))
			{
				return entry.Kind;
			}
		}

		return v6
			? new AddressKind("Reserved", "::/0", 0, false, "Not yet allocated by IANA.")
			: new AddressKind("Public", "0.0.0.0/0", 0, true);
	}

	private static Entry[] Sorted(Entry[] entries) => [.. entries.OrderByDescending(entry => entry.Subnet.PrefixLength)];

	private sealed class Entry
	{
		public Entry(string range, string name, bool isPublic, string? note = null)
		{
			if (!SubnetMath.TryCalculate(range, out SubnetInfo? subnet, out string? problem))
			{
				throw new InvalidOperationException(problem);
			}

			Subnet = subnet;
			Kind = new AddressKind(name, range, subnet.PrefixLength, isPublic, note);
		}

		public SubnetInfo Subnet { get; }

		public AddressKind Kind { get; }
	}
}
