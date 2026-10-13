using System.Buffers.Binary;

namespace Moka.DevTools.Hashing;

/// <summary>
/// CRC-32 as zip, gzip and Ethernet compute it: the reflected polynomial 0xEDB88320, starting from all ones and inverted at
/// the end. The value is written big-endian, which is how it is printed everywhere ("cbf43926" for "123456789").
/// </summary>
internal sealed class Crc32
{
	private static readonly uint[] Table = BuildTable();

	private uint _crc = 0xFFFFFFFF;

	public static uint Compute(ReadOnlySpan<byte> data)
	{
		Crc32 crc = new();
		crc.Append(data);
		return ~crc._crc;
	}

	public void Append(ReadOnlySpan<byte> data)
	{
		uint crc = _crc;
		foreach (byte value in data)
		{
			crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
		}

		_crc = crc;
	}

	public byte[] Finish()
	{
		byte[] result = new byte[4];
		BinaryPrimitives.WriteUInt32BigEndian(result, ~_crc);
		return result;
	}

	private static uint[] BuildTable()
	{
		uint[] table = new uint[256];
		for (uint index = 0; index < 256; index++)
		{
			uint value = index;
			for (int bit = 0; bit < 8; bit++)
			{
				value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
			}

			table[index] = value;
		}

		return table;
	}
}
