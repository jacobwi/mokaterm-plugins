using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using Moka.Network.Targets;

namespace Moka.Network.Subnets;

/// <summary>
/// Works out an IPv4 or IPv6 prefix from what was typed: <c>192.168.1.10/24</c>, <c>10.0.0.0/255.0.0.0</c>,
/// <c>2001:db8::/48</c>, or an address alone, which is its own /32 or /128; splits it into smaller prefixes and says
/// whether an address falls inside. Addresses are 128-bit numbers here, an IPv4 one in the low 32 bits, so both families
/// share the arithmetic. Ported from Mokaterm's sample plugin.
/// </summary>
public static class SubnetMath
{
	/// <summary>The most smaller prefixes a split lists; a /48 into /64s is 65,536, which no list helps anyone read.</summary>
	public const int MaxSplitRows = 256;

	/// <summary>Works out <paramref name="text"/>, or says why it cannot be read.</summary>
	public static bool TryCalculate(string? text, [NotNullWhen(true)] out SubnetInfo? info, [NotNullWhen(false)] out string? problem)
	{
		info = null;
		string input = (text ?? "").Trim();
		if (input.Length == 0)
		{
			problem = "Type an address with a prefix, such as 192.168.1.10/24 or 2001:db8::/48.";
			return false;
		}

		int slash = input.IndexOf('/', StringComparison.Ordinal);
		string host = slash < 0 ? input : input[..slash].Trim();
		if (!TryParseAddress(host, out IPAddress? address))
		{
			problem = $"{host} is not an IPv4 or IPv6 address.";
			return false;
		}

		int bits = address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
		int prefix = bits;
		if (slash >= 0 && !TryParsePrefix(input[(slash + 1)..].Trim(), address.AddressFamily, bits, out prefix, out problem))
		{
			return false;
		}

		info = Calculate(address, prefix);
		problem = null;
		return true;
	}

	/// <summary>The prefix of <paramref name="prefixLength"/> bits that holds <paramref name="address"/>.</summary>
	public static SubnetInfo Calculate(IPAddress address, int prefixLength)
	{
		ArgumentNullException.ThrowIfNull(address);
		bool v4 = address.AddressFamily == AddressFamily.InterNetwork;
		int bits = v4 ? 32 : 128;
		ArgumentOutOfRangeException.ThrowIfNegative(prefixLength);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(prefixLength, bits);

		UInt128 hostMask = HostMask(prefixLength, v4);
		UInt128 mask = All(v4) & ~hostMask;
		UInt128 value = ToNumber(address);
		UInt128 network = value & mask;
		UInt128 last = network | hostMask;
		BigInteger count = BigInteger.One << (bits - prefixLength);

		// RFC 3021 gives both addresses of an IPv4 /31 to hosts, and a /32 is the host itself; below those the first and
		// last address of an IPv4 network are not hosts. IPv6 has no broadcast address.
		bool reserved = v4 && prefixLength < 31;
		return new SubnetInfo
		{
			Address = address,
			PrefixLength = prefixLength,
			Network = ToAddress(network, v4),
			Mask = ToAddress(mask, v4),
			Wildcard = v4 ? ToAddress(hostMask, v4) : null,
			Broadcast = reserved ? ToAddress(last, v4) : null,
			FirstHost = ToAddress(reserved ? network + UInt128.One : network, v4),
			LastHost = ToAddress(reserved ? last - UInt128.One : last, v4),
			AddressCount = count,
			HostCount = reserved ? count - 2 : count,
		};
	}

	/// <summary>
	/// True when <paramref name="address"/> lies inside <paramref name="subnet"/>. An IPv4 address written as IPv6
	/// (<c>::ffff:10.0.0.1</c>) is compared as the IPv4 address it is.
	/// </summary>
	public static bool Contains(SubnetInfo subnet, IPAddress address)
	{
		ArgumentNullException.ThrowIfNull(subnet);
		ArgumentNullException.ThrowIfNull(address);
		if (address.IsIPv4MappedToIPv6 && !subnet.IsIPv6)
		{
			address = address.MapToIPv4();
		}

		if ((address.AddressFamily == AddressFamily.InterNetworkV6) != subnet.IsIPv6)
		{
			return false;
		}

		bool v4 = !subnet.IsIPv6;
		UInt128 mask = All(v4) & ~HostMask(subnet.PrefixLength, v4);
		return (ToNumber(address) & mask) == ToNumber(subnet.Network);
	}

	/// <summary>
	/// What the split box asks for, as a prefix length: a count of subnets (rounded up to a power of two) such as
	/// <c>4</c>, or a longer prefix such as <c>/26</c>.
	/// </summary>
	public static bool TryParseSplit(string? text, SubnetInfo subnet, out int prefixLength, [NotNullWhen(false)] out string? problem)
	{
		ArgumentNullException.ThrowIfNull(subnet);
		prefixLength = 0;
		string input = (text ?? "").Trim();
		if (input.StartsWith('/'))
		{
			if (!int.TryParse(input[1..], NumberStyles.None, CultureInfo.InvariantCulture, out prefixLength) || prefixLength <= subnet.PrefixLength || prefixLength > subnet.Bits)
			{
				problem = $"Split into a prefix longer than /{subnet.PrefixLength} and at most /{subnet.Bits}.";
				return false;
			}

			problem = null;
			return true;
		}

		if (!int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 2)
		{
			problem = "Type how many subnets, such as 4, or a prefix length, such as /26.";
			return false;
		}

		int extra = BitOperations.Log2((uint)count - 1) + 1;
		prefixLength = subnet.PrefixLength + extra;
		if (prefixLength > subnet.Bits)
		{
			problem = $"A /{subnet.PrefixLength} cannot be split into {count.ToString("N0", CultureInfo.CurrentCulture)} subnets.";
			return false;
		}

		problem = null;
		return true;
	}

	/// <summary>The first <paramref name="max"/> prefixes of <paramref name="prefixLength"/> bits that make up <paramref name="subnet"/>, and how many there are in all.</summary>
	public static IReadOnlyList<SubnetInfo> Split(SubnetInfo subnet, int prefixLength, int max, out BigInteger total)
	{
		ArgumentNullException.ThrowIfNull(subnet);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(prefixLength, subnet.PrefixLength);
		ArgumentOutOfRangeException.ThrowIfGreaterThan(prefixLength, subnet.Bits);
		bool v4 = !subnet.IsIPv6;
		total = BigInteger.One << (prefixLength - subnet.PrefixLength);
		UInt128 step = UInt128.One << (subnet.Bits - prefixLength);
		UInt128 start = ToNumber(subnet.Network);
		int rows = total > max ? max : (int)total;
		List<SubnetInfo> parts = new(rows);
		for (int index = 0; index < rows; index++)
		{
			parts.Add(Calculate(ToAddress(start + (step * (UInt128)index), v4), prefixLength));
		}

		return parts;
	}

	/// <summary>An IPv6 address with every group written out in four digits: <c>2001:0db8:0000:...</c>.</summary>
	public static string Expanded(IPAddress address)
	{
		ArgumentNullException.ThrowIfNull(address);
		byte[] bytes = address.GetAddressBytes();
		if (bytes.Length != 16)
		{
			return address.ToString();
		}

		StringBuilder text = new(39);
		for (int index = 0; index < 16; index += 2)
		{
			if (index > 0)
			{
				text.Append(':');
			}

			text.Append(bytes[index].ToString("x2", CultureInfo.InvariantCulture)).Append(bytes[index + 1].ToString("x2", CultureInfo.InvariantCulture));
		}

		return text.ToString();
	}

	/// <summary>An IPv4 mask in binary, dotted by octet: <c>11111111.11111111.11111111.00000000</c>.</summary>
	public static string Binary(IPAddress address)
	{
		ArgumentNullException.ThrowIfNull(address);
		return string.Join('.', address.GetAddressBytes().Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
	}

	internal static UInt128 ToNumber(IPAddress address)
	{
		UInt128 value = UInt128.Zero;
		foreach (byte part in address.GetAddressBytes())
		{
			value = (value << 8) | part;
		}

		return value;
	}

	internal static IPAddress ToAddress(UInt128 value, bool v4)
	{
		byte[] bytes = new byte[v4 ? 4 : 16];
		for (int index = bytes.Length - 1; index >= 0; index--)
		{
			bytes[index] = (byte)value;
			value >>= 8;
		}

		return new IPAddress(bytes);
	}

	private static UInt128 All(bool v4) => v4 ? uint.MaxValue : UInt128.MaxValue;

	private static UInt128 HostMask(int prefixLength, bool v4)
	{
		int bits = v4 ? 32 : 128;
		return prefixLength == bits ? UInt128.Zero : All(v4) >> prefixLength;
	}

	/// <summary>An address as <see cref="AddressText"/> reads it, without an IPv6 zone, which names a link and not a part of the prefix.</summary>
	private static bool TryParseAddress(string text, [NotNullWhen(true)] out IPAddress? address)
	{
		address = null;
		if (!AddressText.TryParse(text, out IPAddress? parsed))
		{
			return false;
		}

		address = parsed.AddressFamily == AddressFamily.InterNetworkV6 && parsed.ScopeId != 0 ? new IPAddress(parsed.GetAddressBytes()) : parsed;
		return true;
	}

	/// <summary>A prefix length, or for IPv4 a dotted mask whose set bits all come first.</summary>
	private static bool TryParsePrefix(string text, AddressFamily family, int bits, out int prefix, [NotNullWhen(false)] out string? problem)
	{
		prefix = 0;
		if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int length))
		{
			if (length <= bits)
			{
				prefix = length;
				problem = null;
				return true;
			}

			problem = $"An {(bits == 32 ? "IPv4" : "IPv6")} prefix is 0 to {bits} bits long.";
			return false;
		}

		if (family == AddressFamily.InterNetwork && AddressText.IsDotted(text) && IPAddress.TryParse(text, out IPAddress? mask))
		{
			uint value = (uint)ToNumber(mask);
			int ones = BitOperations.PopCount(value);
			if (value == (ones == 0 ? 0u : uint.MaxValue << (32 - ones)))
			{
				prefix = ones;
				problem = null;
				return true;
			}

			problem = $"{text} is not a mask: its set bits have to come first, as in 255.255.255.0.";
			return false;
		}

		problem = $"{(text.Length > 40 ? text[..40] + "..." : text)} is not a prefix length.";
		return false;
	}
}
