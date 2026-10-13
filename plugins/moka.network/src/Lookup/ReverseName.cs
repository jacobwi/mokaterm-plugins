using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Moka.Network.Lookup;

/// <summary>The PTR name for an address: octets reversed under in-addr.arpa, or nibbles reversed under ip6.arpa.</summary>
public static class ReverseName
{
	public static string For(IPAddress address)
	{
		ArgumentNullException.ThrowIfNull(address);
		if (address.IsIPv4MappedToIPv6)
		{
			address = address.MapToIPv4();
		}

		byte[] bytes = address.GetAddressBytes();
		StringBuilder name = new();
		if (address.AddressFamily == AddressFamily.InterNetwork)
		{
			for (int index = bytes.Length - 1; index >= 0; index--)
			{
				name.Append(bytes[index].ToString(CultureInfo.InvariantCulture)).Append('.');
			}

			return name.Append("in-addr.arpa").ToString();
		}

		for (int index = bytes.Length - 1; index >= 0; index--)
		{
			name.Append(Nibble(bytes[index] & 0xF)).Append('.').Append(Nibble(bytes[index] >> 4)).Append('.');
		}

		return name.Append("ip6.arpa").ToString();
	}

	private static char Nibble(int value) => (char)(value < 10 ? '0' + value : 'a' + value - 10);
}
