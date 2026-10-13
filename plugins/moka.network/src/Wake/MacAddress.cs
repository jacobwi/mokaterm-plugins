using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Moka.Network.Wake;

/// <summary>
/// A network card's hardware address, read from any of the usual forms: <c>aa:bb:cc:dd:ee:ff</c>,
/// <c>aa-bb-cc-dd-ee-ff</c>, Cisco's <c>aabb.ccdd.eeff</c> and bare <c>aabbccddeeff</c>, in either case, and with
/// colons or dashes also the short groups some tools print (<c>a:b:c:d:e:f</c>). Addresses no card has are refused.
/// </summary>
public sealed class MacAddress
{
	public const int Length = 6;

	private readonly byte[] _bytes;

	private MacAddress(byte[] bytes) => _bytes = bytes;

	public ReadOnlySpan<byte> Bytes => _bytes;

	public static bool TryParse(string? text, [NotNullWhen(true)] out MacAddress? address, [NotNullWhen(false)] out string? problem)
	{
		address = null;
		string input = (text ?? "").Trim();
		if (input.Length == 0)
		{
			problem = "Type the machine's MAC address, such as 00:11:22:33:44:55.";
			return false;
		}

		if (input.Length > 32 || !TryReadBytes(input, out byte[]? bytes))
		{
			problem = "That is not a MAC address. Write six pairs of hex digits, such as 00:11:22:33:44:55, 00-11-22-33-44-55 or 0011.2233.4455.";
			return false;
		}

		if (bytes.All(b => b == 0xFF))
		{
			problem = "FF:FF:FF:FF:FF:FF is the broadcast address, not a network card's.";
			return false;
		}

		if (bytes.All(b => b == 0))
		{
			problem = "00:00:00:00:00:00 is not a network card's address.";
			return false;
		}

		if ((bytes[0] & 1) != 0)
		{
			problem = $"{Format(bytes)} is a multicast address, which names a group, not a network card.";
			return false;
		}

		address = new MacAddress(bytes);
		problem = null;
		return true;
	}

	/// <summary>Upper case pairs with colons: <c>00:11:22:AA:BB:CC</c>.</summary>
	public override string ToString() => Format(_bytes);

	private static string Format(byte[] bytes) => string.Join(':', bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

	private static bool TryReadBytes(string input, [NotNullWhen(true)] out byte[]? bytes)
	{
		bytes = null;
		string[] groups;
		if (input.Contains(':', StringComparison.Ordinal) || input.Contains('-', StringComparison.Ordinal))
		{
			char separator = input.Contains(':', StringComparison.Ordinal) ? ':' : '-';
			groups = input.Split(separator);
			if (groups.Length != Length || groups.Any(group => group.Length is 0 or > 2))
			{
				return false;
			}
		}
		else if (input.Contains('.', StringComparison.Ordinal))
		{
			string[] quads = input.Split('.');
			if (quads.Length != 3 || quads.Any(quad => quad.Length != 4))
			{
				return false;
			}

			groups = [.. quads.SelectMany(quad => new[] { quad[..2], quad[2..] })];
		}
		else
		{
			if (input.Length != Length * 2)
			{
				return false;
			}

			groups = [.. Enumerable.Range(0, Length).Select(index => input.Substring(index * 2, 2))];
		}

		byte[] parsed = new byte[Length];
		for (int index = 0; index < Length; index++)
		{
			if (!groups[index].All(char.IsAsciiHexDigit)
				|| !byte.TryParse(groups[index], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out parsed[index]))
			{
				return false;
			}
		}

		bytes = parsed;
		return true;
	}
}
