using System.Globalization;
using System.Text;

namespace Moka.Network.Wake;

/// <summary>The Wake-on-LAN magic packet: six bytes of FF, then the card's address sixteen times, 102 bytes in all.</summary>
public static class MagicPacket
{
	public const int Size = 6 + (16 * MacAddress.Length);

	public static byte[] Build(MacAddress address)
	{
		ArgumentNullException.ThrowIfNull(address);
		byte[] packet = new byte[Size];
		packet.AsSpan(0, 6).Fill(0xFF);
		for (int copy = 0; copy < 16; copy++)
		{
			address.Bytes.CopyTo(packet.AsSpan(6 + (copy * MacAddress.Length)));
		}

		return packet;
	}

	/// <summary>The packet as hex, six bytes to a line, the way the view shows exactly what was sent.</summary>
	public static string Hex(ReadOnlySpan<byte> packet)
	{
		StringBuilder text = new(packet.Length * 3);
		for (int index = 0; index < packet.Length; index++)
		{
			if (index > 0)
			{
				text.Append(index % 6 == 0 ? '\n' : ' ');
			}

			text.Append(packet[index].ToString("X2", CultureInfo.InvariantCulture));
		}

		return text.ToString();
	}
}
